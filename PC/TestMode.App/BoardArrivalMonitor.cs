using System.IO.Ports;

namespace TestMode.App;

internal sealed class BoardArrivalMonitor : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private readonly Func<Task> boardArrived;
    private HashSet<string> knownPorts = new(StringComparer.OrdinalIgnoreCase);
    private bool polling;

    internal BoardArrivalMonitor(Func<Task> boardArrived)
    {
        this.boardArrived = boardArrived;
        timer.Tick += async (_, _) => await PollAsync();
    }

    internal void Start()
    {
        Rearm();
        timer.Start();
    }

    internal void Rearm() => knownPorts = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task PollAsync()
    {
        if (polling) return;
        polling = true;
        try
        {
            var current = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = current.Except(knownPorts, StringComparer.OrdinalIgnoreCase).ToArray();
            knownPorts = current;
            foreach (var port in added)
            {
                await boardArrived();
                break;
            }
        }
        finally
        {
            polling = false;
        }
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
    }
}
