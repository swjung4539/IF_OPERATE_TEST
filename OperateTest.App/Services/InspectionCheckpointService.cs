using System.Text.Json;
using OperateTest.App.Models;

namespace OperateTest.App.Services;

public sealed class InspectionCheckpointService
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Save(InspectionSession session)
    {
        if (session.Results.IsFinalized && session.Results.WorkbookSavedAt.HasValue)
        {
            Delete(session);
            return CheckpointPath(session);
        }

        var path = CheckpointPath(session);
        var temp = path + ".tmp";
        var document = CheckpointDocument.FromSession(session);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temp, path, true);
            return path;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public InspectionSession Load(string checkpointPath, InspectionBoardType expectedBoardType)
    {
        if (!File.Exists(checkpointPath))
            throw new FileNotFoundException("검사 체크포인트 파일을 찾을 수 없습니다.", checkpointPath);

        CheckpointDocument document;
        try
        {
            document = JsonSerializer.Deserialize<CheckpointDocument>(File.ReadAllText(checkpointPath), JsonOptions)
                ?? throw new InvalidDataException("체크포인트 내용이 비어 있습니다.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("검사 체크포인트 파일이 손상되었거나 지원하지 않는 형식입니다.", ex);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException($"지원하지 않는 체크포인트 버전입니다. ({document.Version})");
        if (document.BoardType != expectedBoardType)
            throw new InvalidDataException($"선택한 보드와 체크포인트가 다릅니다. 이 파일은 {BoardName(document.BoardType)}용입니다.");
        if (!File.Exists(document.ResultPath))
            throw new FileNotFoundException("체크포인트에 연결된 Excel 결과 파일을 찾을 수 없습니다.", document.ResultPath);
        if (document.RowNumber is < 6 or > 35)
            throw new InvalidDataException("체크포인트의 Excel 기록 행이 올바르지 않습니다.");

        var session = document.ToSession();
        var validation = new InspectionWorkbookService().ValidateSessionTarget(session);
        if (!validation.IsValid)
            throw new InvalidDataException(validation.Message);

        NormalizeInterruptedState(session.Results);
        Save(session);
        return session;
    }

    public void Delete(InspectionSession session)
    {
        var path = CheckpointPath(session);
        if (File.Exists(path)) File.Delete(path);
    }

    public void TrySave(InspectionSession session)
    {
        try { Save(session); } catch { }
    }

    public void TryDelete(InspectionSession session)
    {
        try { Delete(session); } catch { }
    }

    public static string CheckpointPath(InspectionSession session)
    {
        var directory = Path.GetDirectoryName(session.ResultPath)
            ?? throw new InvalidOperationException("결과 파일 폴더를 확인할 수 없습니다.");
        var resultName = Path.GetFileNameWithoutExtension(session.ResultPath);
        var safeSerial = string.Concat(session.SerialNumber.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return Path.Combine(directory, $".{resultName}.{safeSerial}.inspection.json");
    }

    private static void NormalizeInterruptedState(InspectionResultState results)
    {
        if (results.ManualStatus == InspectionExecutionStatus.Running) results.ManualStatus = InspectionExecutionStatus.Stopped;
        if (results.CommunicationStatus == InspectionExecutionStatus.Running) results.CommunicationStatus = InspectionExecutionStatus.Stopped;
        if (results.DioLoopbackStatus == InspectionExecutionStatus.Running) results.DioLoopbackStatus = InspectionExecutionStatus.Stopped;
        if (results.RotaryStatus == InspectionExecutionStatus.Running) results.RotaryStatus = InspectionExecutionStatus.Stopped;
        if (results.DipStatus == InspectionExecutionStatus.Running) results.DipStatus = InspectionExecutionStatus.Stopped;
        if (results.FaultStatus == InspectionExecutionStatus.Running) results.FaultStatus = InspectionExecutionStatus.Stopped;
        if (results.DioSafetyStatus == DioSafetyStatus.Restoring) results.DioSafetyStatus = DioSafetyStatus.Failed;
        results.InvalidateFinalization();
    }

    private static string BoardName(InspectionBoardType type) =>
        type == InspectionBoardType.Communication ? "COMM" : "DIO";

    public sealed class CheckpointDocument
    {
        public int Version { get; set; } = CurrentVersion;
        public InspectionBoardType BoardType { get; set; }
        public string TemplatePath { get; set; } = string.Empty;
        public string ResultPath { get; set; } = string.Empty;
        public DateTime InspectionDate { get; set; }
        public string Inspector { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public int RowNumber { get; set; }
        public ResultDocument Results { get; set; } = new();

        public static CheckpointDocument FromSession(InspectionSession session) => new()
        {
            BoardType = session.BoardType,
            TemplatePath = session.TemplatePath,
            ResultPath = session.ResultPath,
            InspectionDate = session.InspectionDate,
            Inspector = session.Inspector,
            SerialNumber = session.SerialNumber,
            RowNumber = session.RowNumber,
            Results = ResultDocument.FromState(session.Results)
        };

        public InspectionSession ToSession()
        {
            var session = new InspectionSession(BoardType, TemplatePath, ResultPath, InspectionDate, Inspector, SerialNumber, RowNumber);
            Results.ApplyTo(session.Results);
            return session;
        }
    }

    public sealed class ResultDocument
    {
        public Dictionary<string, InspectionItemResult> VisualItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, InspectionItemResult> PowerItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ManualInspectionCompleted { get; set; }
        public InspectionExecutionStatus ManualStatus { get; set; }
        public bool? CommunicationPassed { get; set; }
        public InspectionExecutionStatus CommunicationStatus { get; set; }
        public bool? DioInputPassed { get; set; }
        public bool? DioOutputPassed { get; set; }
        public InspectionExecutionStatus DioLoopbackStatus { get; set; }
        public DioSafetyStatus DioSafetyStatus { get; set; }
        public bool? RotarySwitchPassed { get; set; }
        public InspectionExecutionStatus RotaryStatus { get; set; }
        public bool? DipSwitchPassed { get; set; }
        public InspectionExecutionStatus DipStatus { get; set; }
        public bool? FaultPassed { get; set; }
        public InspectionExecutionStatus FaultStatus { get; set; }

        public static ResultDocument FromState(InspectionResultState state) => new()
        {
            VisualItems = CloneItems(state.VisualItems), PowerItems = CloneItems(state.PowerItems),
            ManualInspectionCompleted = state.ManualInspectionCompleted, ManualStatus = state.ManualStatus,
            CommunicationPassed = state.CommunicationPassed, CommunicationStatus = state.CommunicationStatus,
            DioInputPassed = state.DioInputPassed, DioOutputPassed = state.DioOutputPassed,
            DioLoopbackStatus = state.DioLoopbackStatus, DioSafetyStatus = state.DioSafetyStatus,
            RotarySwitchPassed = state.RotarySwitchPassed, RotaryStatus = state.RotaryStatus,
            DipSwitchPassed = state.DipSwitchPassed, DipStatus = state.DipStatus,
            FaultPassed = state.FaultPassed, FaultStatus = state.FaultStatus
        };

        public void ApplyTo(InspectionResultState state)
        {
            CopyItems(VisualItems, state.VisualItems); CopyItems(PowerItems, state.PowerItems);
            state.ManualInspectionCompleted = ManualInspectionCompleted; state.ManualStatus = ManualStatus;
            state.CommunicationPassed = CommunicationPassed; state.CommunicationStatus = CommunicationStatus;
            state.DioInputPassed = DioInputPassed; state.DioOutputPassed = DioOutputPassed;
            state.DioLoopbackStatus = DioLoopbackStatus; state.DioSafetyStatus = DioSafetyStatus;
            state.RotarySwitchPassed = RotarySwitchPassed; state.RotaryStatus = RotaryStatus;
            state.DipSwitchPassed = DipSwitchPassed; state.DipStatus = DipStatus;
            state.FaultPassed = FaultPassed; state.FaultStatus = FaultStatus;
        }

        private static Dictionary<string, InspectionItemResult> CloneItems(IReadOnlyDictionary<string, InspectionItemResult> source) =>
            source.ToDictionary(x => x.Key, x => new InspectionItemResult { Passed = x.Value.Passed, Measurement = x.Value.Measurement }, StringComparer.OrdinalIgnoreCase);

        private static void CopyItems(IReadOnlyDictionary<string, InspectionItemResult> source, Dictionary<string, InspectionItemResult> destination)
        {
            destination.Clear();
            foreach (var item in source)
                destination[item.Key] = new InspectionItemResult { Passed = item.Value.Passed, Measurement = item.Value.Measurement };
        }
    }
}
