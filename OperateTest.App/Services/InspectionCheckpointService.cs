using System.Text.Json;
using OperateTest.App.Models;

namespace OperateTest.App.Services;

public sealed class InspectionCheckpointService
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static event EventHandler<CheckpointSaveEventArgs>? SaveActivity;

    public string Save(InspectionSession session)
    {
        if (session.Results.IsFinalized && session.Results.WorkbookSavedAt.HasValue)
        {
            Delete(session);
            return CheckpointPath(session);
        }

        var path = CheckpointPath(session);
        var temp = path + ".tmp";
        var backup = BackupPath(path);
        var document = CheckpointDocument.FromSession(session);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions));
            if (File.Exists(path)) File.Copy(path, backup, true);
            File.Move(temp, path, true);
            RaiseSaveActivity(new CheckpointSaveEventArgs(session.SessionId, true, DateTime.Now, path, "자동 저장 완료"));
            new InspectionAuditService().TryRecord(session, "checkpoint_saved", "checkpoint", "success", ResultSummary(session));
            return path;
        }
        catch (Exception ex)
        {
            RaiseSaveActivity(new CheckpointSaveEventArgs(session.SessionId, false, DateTime.Now, path, ex.Message));
            new InspectionAuditService().TryRecord(session, "checkpoint_failed", "checkpoint", "error", ex.Message, ex);
            throw;
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
        var recoveredFromBackup = false;
        try
        {
            document = ReadDocument(checkpointPath);
        }
        catch (Exception original) when (original is JsonException or InvalidDataException or IOException)
        {
            var backupPath = BackupPath(checkpointPath);
            if (!File.Exists(backupPath))
                throw new InvalidDataException("검사 체크포인트 파일이 손상되었고 복구용 백업도 없습니다.", original);

            try
            {
                document = ReadDocument(backupPath);
                ValidateDocument(document, expectedBoardType);
                PreserveCorruptCheckpoint(checkpointPath);
                File.Copy(backupPath, checkpointPath, true);
                recoveredFromBackup = true;
            }
            catch (Exception recoveryException)
            {
                throw new InvalidDataException("검사 체크포인트와 복구용 백업을 모두 읽지 못했습니다.", recoveryException);
            }
        }

        ValidateDocument(document, expectedBoardType);

        var session = document.ToSession();
        var validation = new InspectionWorkbookService().ValidateSessionTarget(session);
        if (!validation.IsValid)
            throw new InvalidDataException(validation.Message);

        NormalizeInterruptedState(session.Results);
        Save(session);
        new InspectionAuditService().TryRecord(
            session,
            recoveredFromBackup ? "checkpoint_recovered" : "checkpoint_loaded",
            "checkpoint",
            "success",
            recoveredFromBackup ? "손상 파일을 보존하고 백업 체크포인트에서 복구" : "중단 검사 복구 완료");
        return session;
    }

    public void Delete(InspectionSession session)
    {
        var path = CheckpointPath(session);
        if (File.Exists(path)) File.Delete(path);
        var backup = BackupPath(path);
        if (File.Exists(backup)) File.Delete(backup);
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

    public static string BackupPath(string checkpointPath) => checkpointPath + ".bak";

    private static CheckpointDocument ReadDocument(string path) =>
        JsonSerializer.Deserialize<CheckpointDocument>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException("체크포인트 내용이 비어 있습니다.");

    private static void ValidateDocument(CheckpointDocument document, InspectionBoardType expectedBoardType)
    {
        if (document.Version != CurrentVersion)
            throw new InvalidDataException($"지원하지 않는 체크포인트 버전입니다. ({document.Version})");
        if (document.BoardType != expectedBoardType)
            throw new InvalidDataException($"선택한 보드와 체크포인트가 다릅니다. 이 파일은 {BoardName(document.BoardType)}용입니다.");
        if (!File.Exists(document.ResultPath))
            throw new FileNotFoundException("체크포인트에 연결된 Excel 결과 파일을 찾을 수 없습니다.", document.ResultPath);
        if (document.RowNumber is < 6 or > 35)
            throw new InvalidDataException("체크포인트의 Excel 기록 행이 올바르지 않습니다.");
    }

    private static void PreserveCorruptCheckpoint(string path)
    {
        if (!File.Exists(path)) return;
        var preserved = path + $".corrupt.{DateTime.Now:yyyyMMddHHmmssfff}";
        File.Move(path, preserved);
    }

    private static string ResultSummary(InspectionSession session) =>
        $"manual={session.Results.ManualStatus};comm={session.Results.CommunicationStatus};dio={session.Results.DioLoopbackStatus};rotary={session.Results.RotaryStatus};dip={session.Results.DipStatus};fault={session.Results.FaultStatus}";

    private static void RaiseSaveActivity(CheckpointSaveEventArgs args)
    {
        if (SaveActivity is null) return;
        foreach (EventHandler<CheckpointSaveEventArgs> handler in SaveActivity.GetInvocationList())
        {
            try { handler(null, args); } catch { }
        }
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
        public Guid SessionId { get; set; }
        public Guid? PreviousSessionId { get; set; }
        public DateTime StartedAt { get; set; }
        public string ApplicationVersion { get; set; } = string.Empty;
        public string TemplateProfileVersion { get; set; } = string.Empty;
        public string TemplateSha256 { get; set; } = string.Empty;
        public Dictionary<string, int> AttemptCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
            SessionId = session.SessionId,
            PreviousSessionId = session.PreviousSessionId,
            StartedAt = session.StartedAt,
            ApplicationVersion = session.ApplicationVersion,
            TemplateProfileVersion = session.TemplateProfileVersion,
            TemplateSha256 = session.TemplateSha256,
            AttemptCounts = new Dictionary<string, int>(session.AttemptCounts, StringComparer.OrdinalIgnoreCase),
            Results = ResultDocument.FromState(session.Results)
        };

        public InspectionSession ToSession()
        {
            var session = new InspectionSession(BoardType, TemplatePath, ResultPath, InspectionDate, Inspector, SerialNumber, RowNumber);
            session.SessionId = SessionId == Guid.Empty ? Guid.NewGuid() : SessionId;
            session.PreviousSessionId = PreviousSessionId;
            session.StartedAt = StartedAt == default ? DateTime.Now : StartedAt;
            session.ApplicationVersion = ApplicationVersion;
            session.TemplateProfileVersion = TemplateProfileVersion;
            session.TemplateSha256 = TemplateSha256;
            foreach (var attempt in AttemptCounts)
                session.AttemptCounts[attempt.Key] = attempt.Value;
            Results.ApplyTo(session.Results);
            return session;
        }
    }

    public sealed class ResultDocument
    {
        public Dictionary<string, InspectionItemResult> VisualItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, InspectionItemResult> PowerItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DeviceResponseState> DeviceResponses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
            DeviceResponses = CloneResponses(state.DeviceResponses),
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
            CopyResponses(DeviceResponses, state.DeviceResponses);
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

        private static Dictionary<string, DeviceResponseState> CloneResponses(IReadOnlyDictionary<string, DeviceResponseState> source) =>
            source.ToDictionary(
                item => item.Key,
                item => new DeviceResponseState
                {
                    Status = item.Value.Status,
                    CheckedAt = item.Value.CheckedAt,
                    Detail = item.Value.Detail
                },
                StringComparer.OrdinalIgnoreCase);

        private static void CopyResponses(
            IReadOnlyDictionary<string, DeviceResponseState> source,
            Dictionary<string, DeviceResponseState> destination)
        {
            destination.Clear();
            foreach (var item in source)
            {
                destination[item.Key] = new DeviceResponseState
                {
                    Status = item.Value.Status,
                    CheckedAt = item.Value.CheckedAt,
                    Detail = item.Value.Detail
                };
            }
        }
    }
}

public sealed record CheckpointSaveEventArgs(
    Guid SessionId,
    bool Success,
    DateTime Timestamp,
    string Path,
    string Message);
