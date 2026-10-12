using System.Text.Json;
using OperateTest.App.Models;

namespace OperateTest.App.Services;

public sealed class InspectionAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public int BeginAttempt(InspectionSession session, string step, string detail)
    {
        var attempt = session.AttemptCounts.TryGetValue(step, out var current) ? current + 1 : 1;
        session.AttemptCounts[step] = attempt;
        TryRecord(session, "attempt_started", step, "running", detail, attempt: attempt);
        return attempt;
    }

    public void CompleteAttempt(
        InspectionSession session,
        string step,
        int attempt,
        InspectionExecutionStatus status,
        string detail,
        Exception? exception = null) =>
        TryRecord(session, "attempt_completed", step, status.ToString(), detail, exception, attempt);

    public void TryRecord(
        InspectionSession session,
        string eventType,
        string step,
        string status,
        string detail,
        Exception? exception = null,
        int? attempt = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(session.ResultPath)
                ?? throw new InvalidOperationException("결과 파일 폴더를 확인할 수 없습니다.");
            var logDirectory = Path.Combine(directory, ".inspection-logs");
            Directory.CreateDirectory(logDirectory);
            var fileName = Path.GetFileNameWithoutExtension(session.ResultPath) + ".audit.jsonl";
            var path = Path.Combine(logDirectory, fileName);
            var entry = new AuditEntry(
                DateTimeOffset.Now,
                session.ApplicationVersion,
                session.TemplateProfileVersion,
                session.TemplateSha256,
                session.SessionId,
                session.PreviousSessionId,
                session.BoardType.ToString(),
                session.SerialNumber,
                session.Inspector,
                session.RowNumber,
                eventType,
                step,
                attempt,
                status,
                detail,
                exception is null ? null : InspectionErrorClassifier.Classify(exception),
                exception?.GetType().Name,
                exception?.Message);
            File.AppendAllText(path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
        catch
        {
            // 감사 로그 실패가 검사 데이터 저장과 장비 안전 처리를 방해하면 안 된다.
        }
    }

    public static string AuditPath(InspectionSession session)
    {
        var directory = Path.GetDirectoryName(session.ResultPath)
            ?? throw new InvalidOperationException("결과 파일 폴더를 확인할 수 없습니다.");
        return Path.Combine(directory, ".inspection-logs",
            Path.GetFileNameWithoutExtension(session.ResultPath) + ".audit.jsonl");
    }

    private sealed record AuditEntry(
        DateTimeOffset Timestamp,
        string ApplicationVersion,
        string TemplateProfileVersion,
        string TemplateSha256,
        Guid SessionId,
        Guid? PreviousSessionId,
        string BoardType,
        string SerialNumber,
        string Inspector,
        int RowNumber,
        string EventType,
        string Step,
        int? Attempt,
        string Status,
        string Detail,
        string? ErrorCode,
        string? ExceptionType,
        string? ExceptionMessage);
}

public static class InspectionErrorClassifier
{
    public static string Classify(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "FILE_PERMISSION_DENIED",
        FileNotFoundException => "FILE_NOT_FOUND",
        InvalidDataException => "DATA_INVALID",
        OperationCanceledException => "OPERATION_CANCELED",
        TimeoutException => "DEVICE_TIMEOUT",
        IOException io when IsDiskFull(io) => "DISK_FULL",
        IOException => "FILE_OR_PORT_IO",
        _ when exception.Message.Contains("CRC", StringComparison.OrdinalIgnoreCase) => "PROTOCOL_CRC",
        _ when exception.Message.Contains("포트", StringComparison.OrdinalIgnoreCase) => "SERIAL_PORT",
        _ when exception.Message.Contains("ST-LINK", StringComparison.OrdinalIgnoreCase) => "STLINK",
        _ => "UNEXPECTED"
    };

    private static bool IsDiskFull(IOException exception) =>
        (exception.HResult & 0xFFFF) is 0x27 or 0x70;
}
