using System.Reflection;
using System.Security.Cryptography;

namespace OperateTest.App.Services;

public static class ApplicationBuildInfo
{
    public static string Version { get; } = ResolveVersion();
    public static string ExecutablePath => Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "OperateTest.exe");
    public static DateTime BuildFileTime => File.Exists(ExecutablePath)
        ? File.GetLastWriteTime(ExecutablePath)
        : default;
    public static string ExecutableSha256 => ExecutableHash.Value;
    private static readonly Lazy<string> ExecutableHash = new(ResolveExecutableHash);

    private static string ResolveVersion()
    {
        var assembly = typeof(ApplicationBuildInfo).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }

    private static string ResolveExecutableHash()
    {
        try
        {
            using var stream = File.OpenRead(ExecutablePath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex)
        {
            return $"확인 실패 ({ex.GetType().Name})";
        }
    }
}
