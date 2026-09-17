using System.Runtime.InteropServices;

namespace TestMode.App;

internal sealed class TestModeApplicationContext : ApplicationContext
{
    private readonly MainForm form = new();
    private readonly bool watchMode;
    private readonly BoardArrivalMonitor? monitor;
    private readonly NotifyIcon? trayIcon;
    private bool exiting;

    internal TestModeApplicationContext(bool watchMode)
    {
        this.watchMode = watchMode;
        MainForm = form;
        form.FormClosed += (_, _) => ExitThread();
        _ = form.Handle;

        if (!watchMode)
        {
            form.Show();
            return;
        }

        form.FormClosing += OnFormClosing;
        var menu = new ContextMenuStrip();
        menu.Items.Add("TestMode 열기", null, (_, _) => ShowWindow());
        menu.Items.Add("종료", null, (_, _) => ExitWatcher());
        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "MSC-HIFx TestMode USB 감시",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => ShowWindow();
        monitor = new BoardArrivalMonitor(() =>
        {
            ShowWindow();
            return Task.CompletedTask;
        });
        monitor.Start();
    }

    internal void ShowFromAnyThread()
    {
        if (form.IsDisposed) return;
        if (form.IsHandleCreated) form.BeginInvoke(ShowWindow);
    }

    private void ShowWindow()
    {
        if (form.IsDisposed) return;
        form.Navigate(Page.Home);
        if (!form.Visible) form.Show();
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.ShowInTaskbar = true;
        form.Activate();
        form.BringToFront();
        _ = SetForegroundWindow(form.Handle);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (exiting || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
            return;
        e.Cancel = true;
        form.Hide();
        monitor?.Rearm();
    }

    private void ExitWatcher()
    {
        exiting = true;
        form.Close();
    }

    protected override void ExitThreadCore()
    {
        monitor?.Dispose();
        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.ContextMenuStrip?.Dispose();
            trayIcon.Dispose();
        }
        form.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
