namespace OperateTest.App.Models;

public enum InspectionBoardType
{
    Communication,
    DigitalIo
}

public enum InspectionExecutionStatus
{
    NotRun,
    Running,
    Pass,
    Fail,
    Stopped,
    Error
}

public enum DioSafetyStatus
{
    NotChecked,
    Restoring,
    OffVerified,
    CommandSent,
    Failed
}

public sealed record InspectionSession(
    InspectionBoardType BoardType,
    string TemplatePath,
    string ResultPath,
    DateTime InspectionDate,
    string Inspector,
    string SerialNumber,
    int RowNumber)
{
    public InspectionResultState Results { get; } = new();
}

public sealed class InspectionResultState
{
    public Dictionary<string, InspectionItemResult> VisualItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, InspectionItemResult> PowerItems { get; } = new(StringComparer.OrdinalIgnoreCase);
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
    public bool? OverallPassed { get; set; }
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime? WorkbookSavedAt { get; set; }

    public void InvalidateFinalization()
    {
        OverallPassed = null;
        IsFinalized = false;
        FinalizedAt = null;
        WorkbookSavedAt = null;
    }
}

public sealed class InspectionItemResult
{
    public bool? Passed { get; set; }
    public string Measurement { get; set; } = string.Empty;
}

public sealed record WorkbookValidationResult(
    bool IsValid,
    string Message,
    IReadOnlyList<string> SheetNames);
