using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OperateTest.App.Models;

namespace OperateTest.App.Services;

public sealed partial class InspectionWorkbookService
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly string[] CommSheets = ["육안 검사", "전원 검사", "통신 및 기타 검사"];
    private static readonly string[] DioSheets = ["육안 검사", "전원 검사", "DIO&스위치 검사"];

    public WorkbookValidationResult Validate(string path, InspectionBoardType boardType)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Invalid("선택한 기준서 파일을 찾을 수 없습니다.");
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return Invalid("매크로가 없는 Excel 통합 문서(.xlsx)만 선택할 수 있습니다.");

        try
        {
            using var package = WorkbookPackage.OpenRead(path);
            var requiredSheets = RequiredSheets(boardType);
            var missing = requiredSheets.Where(name => !package.Sheets.ContainsKey(name)).ToArray();
            if (missing.Length > 0)
                return Invalid($"필수 시트가 없습니다: {string.Join(", ", missing)}", package.Sheets.Keys);

            foreach (var sheetName in requiredSheets)
            {
                var sheet = package.LoadSheet(sheetName);
                if (!EqualsText(package, sheet, "H1", "검사일자")
                    || !EqualsText(package, sheet, "J1", "검사자")
                    || !EqualsText(package, sheet, "A2", "번호")
                    || !EqualsText(package, sheet, "B2", "시리얼"))
                    return Invalid($"'{sheetName}' 시트의 기본 양식이 지원하는 기준서와 다릅니다.", package.Sheets.Keys);
            }

            var functionSheetName = boardType == InspectionBoardType.Communication
                ? "통신 및 기타 검사"
                : "DIO&스위치 검사";
            var functionSheet = package.LoadSheet(functionSheetName);
            var validFunctionHeaders = boardType == InspectionBoardType.Communication
                ? ContainsText(package, functionSheet, "D2", "통신")
                  && ContainsText(package, functionSheet, "F2", "fault")
                  && ContainsText(package, functionSheet, "H2", "로터리")
                : ContainsText(package, functionSheet, "D2", "Digital Input")
                  && ContainsText(package, functionSheet, "F2", "Digital Output")
                  && ContainsText(package, functionSheet, "H2", "DIP Switch");
            if (!validFunctionHeaders)
                return Invalid($"'{functionSheetName}' 시트의 검사 항목 위치가 지원하는 양식과 다릅니다.", package.Sheets.Keys);

            return new WorkbookValidationResult(true, "지원하는 수입검사 기준서입니다.", package.Sheets.Keys.ToArray());
        }
        catch (InvalidDataException)
        {
            return Invalid("정상적인 .xlsx 파일이 아니거나 파일이 손상되었습니다.");
        }
        catch (IOException ex)
        {
            return Invalid($"기준서를 읽을 수 없습니다. 파일 사용 상태를 확인하세요. ({ex.Message})");
        }
        catch (UnauthorizedAccessException)
        {
            return Invalid("기준서를 읽을 권한이 없습니다.");
        }
        catch (Exception ex)
        {
            return Invalid($"기준서 확인 중 오류가 발생했습니다. ({ex.Message})");
        }
    }

    public InspectionSession CreateSession(
        string templatePath,
        string outputDirectory,
        InspectionBoardType boardType,
        DateTime inspectionDate,
        string inspector,
        string serialNumber)
    {
        var validation = Validate(templatePath, boardType);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Message);

        inspector = inspector.Trim();
        serialNumber = serialNumber.Trim().ToUpperInvariant();
        ValidateInputs(boardType, inspector, serialNumber);

        Directory.CreateDirectory(outputDirectory);
        var boardName = boardType == InspectionBoardType.Communication
            ? "COMMUNICATION_BOARD"
            : "DIGITAL_IO_BOARD";
        var safeInspector = InvalidFileNameCharsRegex().Replace(inspector, "_");
        var resultName = $"수입검사결과_{boardName}_{inspectionDate:yyyyMMdd}_{safeInspector}.xlsx";
        var resultPath = Path.Combine(outputDirectory, resultName);
        var sourcePath = File.Exists(resultPath) ? resultPath : templatePath;

        if (File.Exists(resultPath))
        {
            var existingValidation = Validate(resultPath, boardType);
            if (!existingValidation.IsValid)
                throw new InvalidOperationException($"기존 결과 파일을 이어서 사용할 수 없습니다. {existingValidation.Message}");
        }

        var tempPath = Path.Combine(outputDirectory, $".{Guid.NewGuid():N}.xlsx");
        try
        {
            CopyWithSharedRead(sourcePath, tempPath);
            int rowNumber;
            using (var package = WorkbookPackage.OpenUpdate(tempPath))
            {
                rowNumber = FindAvailableRow(package, boardType, serialNumber);
                foreach (var sheetName in RequiredSheets(boardType))
                {
                    var sheet = package.LoadSheet(sheetName);
                    SetInlineText(sheet, "I1", inspectionDate.ToString("yyyy.MM.dd"));
                    SetInlineText(sheet, "K1", inspector);
                    SetInlineText(sheet, $"A{rowNumber}", (rowNumber - 5).ToString());
                    SetInlineText(sheet, $"B{rowNumber}", serialNumber);
                    package.SaveSheet(sheetName, sheet);
                }
            }

            File.Move(tempPath, resultPath, true);
            return new InspectionSession(
                boardType,
                Path.GetFullPath(templatePath),
                Path.GetFullPath(resultPath),
                inspectionDate.Date,
                inspector,
                serialNumber,
                rowNumber);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public WorkbookValidationResult ValidateSessionTarget(InspectionSession session)
    {
        var validation = Validate(session.ResultPath, session.BoardType);
        if (!validation.IsValid) return validation;
        try
        {
            using var package = WorkbookPackage.OpenRead(session.ResultPath);
            foreach (var sheetName in RequiredSheets(session.BoardType))
            {
                var sheet = package.LoadSheet(sheetName);
                var serial = package.CellText(sheet, $"B{session.RowNumber}").Trim();
                if (!string.Equals(serial, session.SerialNumber, StringComparison.OrdinalIgnoreCase))
                    return Invalid($"'{sheetName}' 시트의 {session.RowNumber}행 시리얼이 체크포인트와 일치하지 않습니다.", package.Sheets.Keys);
            }
            return new WorkbookValidationResult(true, "검사 체크포인트와 Excel 결과 파일이 일치합니다.", package.Sheets.Keys.ToArray());
        }
        catch (Exception ex)
        {
            return Invalid($"Excel 결과 파일과 체크포인트를 확인할 수 없습니다. ({ex.Message})");
        }
    }

    public void SaveFinalResults(InspectionSession session)
    {
        if (!session.Results.IsFinalized || !session.Results.OverallPassed.HasValue)
            throw new InvalidOperationException("최종 판정을 확정한 후 Excel 결과를 저장할 수 있습니다.");
        if (!File.Exists(session.ResultPath))
            throw new FileNotFoundException("검사 결과 Excel 파일을 찾을 수 없습니다.", session.ResultPath);

        var validation = Validate(session.ResultPath, session.BoardType);
        if (!validation.IsValid)
            throw new InvalidOperationException($"검사 결과 파일의 양식이 올바르지 않습니다. {validation.Message}");

        var directory = Path.GetDirectoryName(session.ResultPath)
            ?? throw new InvalidOperationException("결과 파일 경로를 확인할 수 없습니다.");
        var tempPath = Path.Combine(directory, $".{Guid.NewGuid():N}.xlsx");
        try
        {
            CopyWithSharedRead(session.ResultPath, tempPath);
            using (var package = WorkbookPackage.OpenUpdate(tempPath))
            {
                WriteVisualResults(package, session);
                WritePowerResults(package, session);
                WriteFunctionResults(package, session);
            }
            File.Move(tempPath, session.ResultPath, true);
            session.Results.WorkbookSavedAt = DateTime.Now;
            new InspectionCheckpointService().TryDelete(session);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private static void WriteVisualResults(WorkbookPackage package, InspectionSession session)
    {
        var sheet = package.LoadSheet("육안 검사");
        var row = session.RowNumber;
        var columns = new (string Key, string Column)[]
        {
            ("missing", "D"), ("reversed", "E"), ("cold_joint", "F"),
            ("wrong_part", "G"), ("foreign", "H"), ("solder_short", "I"), ("cleaning", "J")
        };
        foreach (var item in columns)
            SetPassFail(sheet, $"{item.Column}{row}", RequiredResult(session.Results.VisualItems, item.Key, "육안검사"));
        SetInlineText(sheet, $"K{row}", $"최종 {PassFailWord(session.Results.OverallPassed!.Value)} / 프로그램 기록");
        package.SaveSheet("육안 검사", sheet);
    }

    private static void WritePowerResults(WorkbookPackage package, InspectionSession session)
    {
        var sheet = package.LoadSheet("전원 검사");
        var row = session.RowNumber;
        var columns = session.BoardType == InspectionBoardType.Communication
            ? new (string Key, string Column, string Label)[]
            {
                ("power_led", "D", "LED1"), ("input_24v", "E", "C27"), ("tp1_5v", "F", "TP1-TP3"),
                ("tp2_3v3", "G", "TP2-GND_ISO1"), ("c37_5v", "H", "C37"), ("c41_5v", "I", "C41")
            }
            :
            [
                ("power_led", "D", "LED35"), ("input_24v", "E", "C27"),
                ("c30_5v", "F", "C30"), ("tp3_3v3", "G", "TP3-GND_ISO1")
            ];

        foreach (var item in columns)
            SetPassFail(sheet, $"{item.Column}{row}", RequiredResult(session.Results.PowerItems, item.Key, "전원검사"));

        var measurements = columns
            .Select(item => (item.Label, Result: session.Results.PowerItems[item.Key]))
            .Where(item => !string.IsNullOrWhiteSpace(item.Result.Measurement))
            .Select(item => $"{item.Label}:{item.Result.Measurement.Trim()}");
        var note = string.Join(" / ", measurements);
        SetInlineText(sheet, $"K{row}", string.IsNullOrEmpty(note) ? "검사자 판정" : note);
        package.SaveSheet("전원 검사", sheet);
    }

    private static void WriteFunctionResults(WorkbookPackage package, InspectionSession session)
    {
        var sheetName = session.BoardType == InspectionBoardType.Communication
            ? "통신 및 기타 검사"
            : "DIO&스위치 검사";
        var sheet = package.LoadSheet(sheetName);
        var row = session.RowNumber;
        if (session.BoardType == InspectionBoardType.Communication)
        {
            SetPassFail(sheet, $"D{row}", RequiredResult(session.Results.CommunicationPassed, "통신 검사"));
            SetPassFail(sheet, $"F{row}", RequiredResult(session.Results.FaultPassed, "FAULT 검사"));
            SetPassFail(sheet, $"H{row}", RequiredResult(session.Results.RotarySwitchPassed, "로터리 스위치 검사"));
        }
        else
        {
            SetPassFail(sheet, $"D{row}", RequiredResult(session.Results.DioInputPassed, "Digital Input 검사"));
            SetPassFail(sheet, $"F{row}", RequiredResult(session.Results.DioOutputPassed, "Digital Output 검사"));
            SetPassFail(sheet, $"H{row}", RequiredResult(session.Results.DipSwitchPassed, "DIP Switch 검사"));
        }
        SetInlineText(sheet, $"J{row}", $"{PassFailWord(session.Results.OverallPassed!.Value)} / 프로그램 자동 기록 / SOP 참고");
        package.SaveSheet(sheetName, sheet);
    }

    private static bool RequiredResult(
        IReadOnlyDictionary<string, InspectionItemResult> items,
        string key,
        string section)
    {
        if (!items.TryGetValue(key, out var result) || !result.Passed.HasValue)
            throw new InvalidOperationException($"{section}의 '{key}' 결과가 없습니다.");
        return result.Passed.Value;
    }

    private static bool RequiredResult(bool? value, string itemName) =>
        value ?? throw new InvalidOperationException($"{itemName} 결과가 없습니다.");

    private static void SetPassFail(XDocument sheet, string reference, bool passed) =>
        SetInlineText(sheet, reference, passed ? "■ P / □ F" : "□ P / ■ F");

    private static string PassFailWord(bool passed) => passed ? "PASS" : "FAIL";

    public static string ExpectedSerialExample(InspectionBoardType boardType) =>
        boardType == InspectionBoardType.Communication ? "COM-2609-001" : "DIO-2609-001";

    private static int FindAvailableRow(WorkbookPackage package, InspectionBoardType boardType, string serialNumber)
    {
        var sheet = package.LoadSheet(RequiredSheets(boardType)[0]);
        for (var row = 6; row <= 35; row++)
        {
            var existing = package.CellText(sheet, $"B{row}").Trim();
            if (string.Equals(existing, serialNumber, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"시리얼번호 '{serialNumber}'는 결과 파일에 이미 등록되어 있습니다. (행 {row})");
        }

        for (var row = 6; row <= 35; row++)
            if (string.IsNullOrWhiteSpace(package.CellText(sheet, $"B{row}")))
                return row;

        throw new InvalidOperationException("결과 파일의 입력 가능한 행(1~30번)이 모두 사용되었습니다. 저장 폴더를 변경하거나 새 결과 파일을 사용하세요.");
    }

    private static void ValidateInputs(InspectionBoardType boardType, string inspector, string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(inspector))
            throw new InvalidOperationException("검사자를 입력하세요.");
        if (inspector.Length > 30)
            throw new InvalidOperationException("검사자는 30자 이내로 입력하세요.");

        var pattern = boardType == InspectionBoardType.Communication
            ? CommSerialRegex()
            : DioSerialRegex();
        if (!pattern.IsMatch(serialNumber))
            throw new InvalidOperationException($"시리얼번호 형식이 올바르지 않습니다. 예: {ExpectedSerialExample(boardType)}");
    }

    private static string[] RequiredSheets(InspectionBoardType boardType) =>
        boardType == InspectionBoardType.Communication ? CommSheets : DioSheets;

    private static bool EqualsText(WorkbookPackage package, XDocument sheet, string cell, string expected) =>
        string.Equals(package.CellText(sheet, cell).Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsText(WorkbookPackage package, XDocument sheet, string cell, string expected) =>
        package.CellText(sheet, cell).Contains(expected, StringComparison.OrdinalIgnoreCase);

    private static WorkbookValidationResult Invalid(string message, IEnumerable<string>? sheets = null) =>
        new(false, message, sheets?.ToArray() ?? []);

    private static void CopyWithSharedRead(string sourcePath, string destinationPath)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    private static void SetInlineText(XDocument sheet, string reference, string value)
    {
        var cell = FindOrCreateCell(sheet, reference);
        cell.SetAttributeValue("t", "inlineStr");
        cell.Elements(SpreadsheetNs + "f").Remove();
        cell.Elements(SpreadsheetNs + "v").Remove();
        cell.Elements(SpreadsheetNs + "is").Remove();
        var text = new XElement(SpreadsheetNs + "t", value);
        if (value.Length != value.Trim().Length)
            text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        cell.Add(new XElement(SpreadsheetNs + "is", text));
    }

    private static XElement FindOrCreateCell(XDocument sheet, string reference)
    {
        var existing = sheet.Descendants(SpreadsheetNs + "c")
            .FirstOrDefault(cell => string.Equals((string?)cell.Attribute("r"), reference, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var rowNumber = int.Parse(Regex.Match(reference, @"\d+").Value);
        var sheetData = sheet.Root?.Element(SpreadsheetNs + "sheetData")
            ?? throw new InvalidDataException("워크시트 데이터가 없습니다.");
        var row = sheetData.Elements(SpreadsheetNs + "row")
            .FirstOrDefault(item => (int?)item.Attribute("r") == rowNumber);
        if (row is null)
        {
            row = new XElement(SpreadsheetNs + "row", new XAttribute("r", rowNumber));
            var nextRow = sheetData.Elements(SpreadsheetNs + "row")
                .FirstOrDefault(item => (int?)item.Attribute("r") > rowNumber);
            if (nextRow is null) sheetData.Add(row); else nextRow.AddBeforeSelf(row);
        }

        var created = new XElement(SpreadsheetNs + "c", new XAttribute("r", reference));
        var newColumn = ColumnNumber(reference);
        var nextCell = row.Elements(SpreadsheetNs + "c")
            .FirstOrDefault(item => ColumnNumber((string?)item.Attribute("r") ?? string.Empty) > newColumn);
        if (nextCell is null) row.Add(created); else nextCell.AddBeforeSelf(created);
        return created;
    }

    private static int ColumnNumber(string reference)
    {
        var result = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter))
            result = result * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
        return result;
    }

    [GeneratedRegex(@"^COM-\d{4}-\d{3,}$", RegexOptions.IgnoreCase)]
    private static partial Regex CommSerialRegex();

    [GeneratedRegex(@"^DIO-\d{4}-\d{3,}$", RegexOptions.IgnoreCase)]
    private static partial Regex DioSerialRegex();

    [GeneratedRegex(@"[\\/:*?\""<>|]")]
    private static partial Regex InvalidFileNameCharsRegex();

    private sealed class WorkbookPackage : IDisposable
    {
        private readonly FileStream _stream;
        private readonly ZipArchive _archive;
        private readonly Dictionary<string, string> _sheetPaths;
        private readonly List<string> _sharedStrings;

        private WorkbookPackage(FileStream stream, ZipArchive archive)
        {
            _stream = stream;
            _archive = archive;
            _sharedStrings = LoadSharedStrings();
            _sheetPaths = LoadSheetPaths();
        }

        public IReadOnlyDictionary<string, string> Sheets => _sheetPaths;

        public static WorkbookPackage OpenRead(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return new WorkbookPackage(stream, new ZipArchive(stream, ZipArchiveMode.Read, false));
        }

        public static WorkbookPackage OpenUpdate(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return new WorkbookPackage(stream, new ZipArchive(stream, ZipArchiveMode.Update, false));
        }

        public XDocument LoadSheet(string name) => LoadXml(_sheetPaths[name]);

        public string CellText(XDocument sheet, string reference)
        {
            var cell = sheet.Descendants(SpreadsheetNs + "c")
                .FirstOrDefault(item => string.Equals((string?)item.Attribute("r"), reference, StringComparison.OrdinalIgnoreCase));
            if (cell is null) return string.Empty;
            var type = (string?)cell.Attribute("t");
            if (type == "inlineStr")
                return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value));
            var value = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
            if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < _sharedStrings.Count)
                return _sharedStrings[index];
            return value;
        }

        public void SaveSheet(string name, XDocument document)
        {
            var entry = _archive.GetEntry(_sheetPaths[name])
                ?? throw new InvalidDataException($"워크시트 항목을 찾을 수 없습니다: {name}");
            using var stream = entry.Open();
            stream.SetLength(0);
            document.Save(stream, SaveOptions.DisableFormatting);
        }

        private Dictionary<string, string> LoadSheetPaths()
        {
            var workbook = LoadXml("xl/workbook.xml");
            var relationships = LoadXml("xl/_rels/workbook.xml.rels");
            var relationshipTargets = relationships
                .Descendants(PackageRelationshipNs + "Relationship")
                .ToDictionary(
                    item => (string)item.Attribute("Id")!,
                    item => (string)item.Attribute("Target")!);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in workbook.Descendants(SpreadsheetNs + "sheet"))
            {
                var name = (string?)sheet.Attribute("name");
                var id = (string?)sheet.Attribute(OfficeRelationshipNs + "id");
                if (name is null || id is null || !relationshipTargets.TryGetValue(id, out var target)) continue;
                result[name] = target.StartsWith('/')
                    ? target.TrimStart('/')
                    : "xl/" + target.TrimStart('.', '/');
            }
            return result;
        }

        private List<string> LoadSharedStrings()
        {
            var entry = _archive.GetEntry("xl/sharedStrings.xml");
            if (entry is null) return [];
            var document = LoadXml(entry);
            return document.Descendants(SpreadsheetNs + "si")
                .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
                .ToList();
        }

        private XDocument LoadXml(string entryName)
        {
            var entry = _archive.GetEntry(entryName)
                ?? throw new InvalidDataException($"Excel 내부 파일을 찾을 수 없습니다: {entryName}");
            return LoadXml(entry);
        }

        private static XDocument LoadXml(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        public void Dispose()
        {
            _archive.Dispose();
            _stream.Dispose();
        }
    }
}
