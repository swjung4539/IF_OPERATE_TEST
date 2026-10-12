using OperateTest.App.Models;

namespace OperateTest.App.Services;

public enum WorkflowStageState
{
    Pending,
    Current,
    Complete,
    Attention
}

public sealed record WorkflowStepState(string Name, WorkflowStageState State, string Detail);

public sealed record InspectionWorkflowSnapshot(
    IReadOnlyList<WorkflowStepState> Steps,
    int CompletedItems,
    int TotalItems,
    string NextAction);

public static class InspectionWorkflowService
{
    public static InspectionWorkflowSnapshot Evaluate(
        InspectionSession session,
        bool portsReady,
        string connectionDetail)
    {
        var required = RequiredStatuses(session).ToArray();
        var completed = required.Count(IsTerminal);
        var hasRunning = required.Any(status => status == InspectionExecutionStatus.Running);
        var hasAttention = required.Any(status => status is InspectionExecutionStatus.Error or InspectionExecutionStatus.Stopped);
        var allComplete = completed == required.Length;
        var finalized = session.Results.IsFinalized;
        var saved = finalized && session.Results.WorkbookSavedAt.HasValue;

        var steps = new[]
        {
            new WorkflowStepState("검사 준비", WorkflowStageState.Complete, session.SerialNumber),
            new WorkflowStepState("장치 연결", portsReady ? WorkflowStageState.Complete : WorkflowStageState.Current, connectionDetail),
            new WorkflowStepState("검사 수행", InspectionStage(), $"{completed}/{required.Length} 완료"),
            new WorkflowStepState("결과 확인", saved || finalized ? WorkflowStageState.Complete : allComplete ? WorkflowStageState.Current : WorkflowStageState.Pending,
                finalized ? "판정 확정" : allComplete ? "최종 판정 필요" : "검사 완료 후 확인"),
            new WorkflowStepState("Excel 저장", saved ? WorkflowStageState.Complete : finalized ? WorkflowStageState.Current : WorkflowStageState.Pending,
                saved ? "저장 완료" : "판정 확정 후 저장")
        };

        return new InspectionWorkflowSnapshot(steps, completed, required.Length, NextAction());

        WorkflowStageState InspectionStage()
        {
            if (saved || allComplete) return WorkflowStageState.Complete;
            if (hasAttention) return WorkflowStageState.Attention;
            if (hasRunning || portsReady) return WorkflowStageState.Current;
            return WorkflowStageState.Pending;
        }

        string NextAction()
        {
            if (saved) return "Excel 저장 완료 · 다음 보드 또는 검사 종료를 선택하세요.";
            if (finalized) return "Excel 저장 상태를 확인하세요.";
            if (allComplete) return "모든 필수 검사가 완료되었습니다. 최종 판정을 확인하세요.";
            if (hasAttention) return "중단 또는 오류 항목을 확인하고 해당 검사를 다시 실행하세요.";
            if (!portsReady) return "필요한 통신 포트를 연결한 뒤 장치 응답을 확인하세요.";
            return FirstPendingAction(session);
        }
    }

    public static IReadOnlyList<(string Name, InspectionExecutionStatus Status)> RequiredItems(InspectionSession session)
    {
        var r = session.Results;
        return session.BoardType == InspectionBoardType.Communication
            ?
            [
                ("육안 및 전원", r.ManualStatus),
                ("COMM 통신", r.CommunicationStatus),
                ("로터리 스위치", r.RotaryStatus),
                ("FAULT 접점", r.FaultStatus)
            ]
            :
            [
                ("육안 및 전원", r.ManualStatus),
                ("DI/DO 루프백", r.DioLoopbackStatus),
                ("ID DIP 스위치", r.DipStatus),
                ("DO 전체 OFF", SafetyStatus(r.DioSafetyStatus))
            ];
    }

    private static IEnumerable<InspectionExecutionStatus> RequiredStatuses(InspectionSession session) =>
        RequiredItems(session).Select(item => item.Status);

    private static bool IsTerminal(InspectionExecutionStatus status) =>
        status is InspectionExecutionStatus.Pass or InspectionExecutionStatus.Fail;

    private static string FirstPendingAction(InspectionSession session)
    {
        var pending = RequiredItems(session).FirstOrDefault(item => !IsTerminal(item.Status));
        return pending.Name is null
            ? "최종 판정을 확인하세요."
            : $"다음 권장 단계: {pending.Name} 검사를 진행하세요.";
    }

    public static InspectionExecutionStatus SafetyStatus(DioSafetyStatus status) => status switch
    {
        DioSafetyStatus.OffVerified => InspectionExecutionStatus.Pass,
        DioSafetyStatus.Failed => InspectionExecutionStatus.Error,
        DioSafetyStatus.Restoring => InspectionExecutionStatus.Running,
        _ => InspectionExecutionStatus.NotRun
    };
}
