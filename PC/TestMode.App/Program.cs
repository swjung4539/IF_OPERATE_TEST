namespace TestMode.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var check = args.FirstOrDefault(a => a.StartsWith("--ui-check="));
        if (check is not null)
        {
            using var form = new MainForm(true);
            form.Shown += async (_, _) => await UiCheck.RunAsync(form, check[11..]);
            Application.Run(form);
            return;
        }

        using var singleInstance = new SingleInstanceCoordinator();
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.RequestActivation();
            return;
        }

        using var context = new TestModeApplicationContext(args.Contains("--usb-watch", StringComparer.OrdinalIgnoreCase));
        singleInstance.ActivationRequested += context.ShowFromAnyThread;
        Application.Run(context);
    }
}
