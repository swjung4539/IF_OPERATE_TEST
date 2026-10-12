using OperateTest.App.Models;

namespace OperateTest.App.Services;

public static class DeviceResponseTracker
{
    public static DeviceResponseState Get(InspectionResultState results, string channelKey) =>
        results.DeviceResponses.TryGetValue(channelKey, out var state)
            ? state
            : new DeviceResponseState();

    public static void Update(
        InspectionResultState results,
        string channelKey,
        DeviceResponseStatus status,
        string detail)
    {
        results.DeviceResponses[channelKey] = new DeviceResponseState
        {
            Status = status,
            CheckedAt = status == DeviceResponseStatus.Checking ? null : DateTime.Now,
            Detail = detail
        };
    }

    public static DeviceResponseStatus ClassifyFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return DeviceResponseStatus.NoResponse;
        return detail.Contains("CRC", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("프레임", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("프로토콜", StringComparison.OrdinalIgnoreCase)
            ? DeviceResponseStatus.ProtocolError
            : DeviceResponseStatus.NoResponse;
    }

    public static string StatusText(DeviceResponseState state) => state.Status switch
    {
        DeviceResponseStatus.Checking => "응답 확인 중",
        DeviceResponseStatus.Responding => state.CheckedAt.HasValue
            ? $"응답 정상 · {state.CheckedAt:HH:mm:ss}"
            : "응답 정상",
        DeviceResponseStatus.NoResponse => "장치 무응답",
        DeviceResponseStatus.ProtocolError => "프로토콜 오류",
        _ => "응답 미확인"
    };
}
