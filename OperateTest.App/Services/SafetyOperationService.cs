namespace OperateTest.App.Services;

public static class SafetyOperationService
{
    public static readonly TimeSpan SerialDisconnectTimeout = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan DioOutputOffTimeout = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan FaultRestoreTimeout = TimeSpan.FromSeconds(26);

    public static async Task RunWithTimeoutAsync(
        string operationName,
        TimeSpan timeout,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await operation(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{operationName} 작업이 {timeout.TotalSeconds:0}초 안에 완료되지 않았습니다.", ex);
        }
    }

    public static async Task<T> RunWithTimeoutAsync<T>(
        string operationName,
        TimeSpan timeout,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await operation(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{operationName} 작업이 {timeout.TotalSeconds:0}초 안에 완료되지 않았습니다.", ex);
        }
    }
}
