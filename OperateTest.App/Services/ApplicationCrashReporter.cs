using System.Text;

namespace OperateTest.App.Services;

internal static class ApplicationCrashReporter
{
    private const string ProductDirectoryName = "OperateTest";

    public static void ShowRecoverableError(IWin32Window? owner, string operation, Exception exception)
    {
        var logPath = TryWrite(operation, exception);
        var logMessage = string.IsNullOrWhiteSpace(logPath)
            ? "오류 로그를 저장하지 못했습니다."
            : $"오류 로그: {logPath}";

        MessageBox.Show(
            owner,
            $"{operation} 중 오류가 발생했습니다. 프로그램은 종료되지 않습니다.{Environment.NewLine}{Environment.NewLine}" +
            $"{exception.Message}{Environment.NewLine}{Environment.NewLine}{logMessage}",
            "작업 오류",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public static string? TryWrite(string operation, Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductDirectoryName,
                "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd}.log");

            var entry = new StringBuilder()
                .AppendLine(new string('=', 80))
                .AppendLine($"Time: {DateTimeOffset.Now:O}")
                .AppendLine($"Version: {ApplicationBuildInfo.Version}")
                .AppendLine($"Operation: {operation}")
                .AppendLine($"Exception: {exception.GetType().FullName}")
                .AppendLine(exception.ToString())
                .ToString();
            File.AppendAllText(path, entry, Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
