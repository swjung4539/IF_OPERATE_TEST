using OperateTest.App.Forms;
using OperateTest.App.Services;
using OperateTest.App.Services.StLink;

namespace OperateTest.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        ConfigureUnhandledExceptionLogging();

        using var connections = new SerialConnectionManager(new SerialPortCatalog());
        var protocols = new InspectionProtocolServices(connections);
        var stLinkMemory = new StLinkMemoryReader();
        var dioIdReader = new StLinkDioIdSwitchReader(stLinkMemory);
        var rotaryReader = new StLinkCommRotaryReader(stLinkMemory);
        Application.Run(new HomeForm(connections, protocols, dioIdReader, rotaryReader));
    }

    private static void ConfigureUnhandledExceptionLogging()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
            ApplicationCrashReporter.ShowRecoverableError(null, "화면 처리", args.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                ApplicationCrashReporter.TryWrite("처리되지 않은 프로그램 오류", exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ApplicationCrashReporter.TryWrite("비동기 작업 오류", args.Exception);
            args.SetObserved();
        };
    }
}
