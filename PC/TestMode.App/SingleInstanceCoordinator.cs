namespace TestMode.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\MSC-HIFx.TestMode.App";
    private const string ActivationEventName = @"Local\MSC-HIFx.TestMode.Activate";
    private readonly Mutex mutex;
    private readonly EventWaitHandle activationEvent;
    private readonly CancellationTokenSource cancellation = new();

    internal bool IsFirstInstance { get; }
    internal event Action? ActivationRequested;

    internal SingleInstanceCoordinator()
    {
        mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        if (IsFirstInstance)
            _ = Task.Run(ListenForActivation);
    }

    internal void RequestActivation() => activationEvent.Set();

    private void ListenForActivation()
    {
        var handles = new WaitHandle[] { activationEvent, cancellation.Token.WaitHandle };
        while (WaitHandle.WaitAny(handles) == 0)
            ActivationRequested?.Invoke();
    }

    public void Dispose()
    {
        cancellation.Cancel();
        activationEvent.Set();
        activationEvent.Dispose();
        cancellation.Dispose();
        if (IsFirstInstance) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
