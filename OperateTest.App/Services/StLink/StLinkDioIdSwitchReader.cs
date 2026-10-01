using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OperateTest.App.Services.StLink;

public sealed partial class StLinkMemoryReader
{
    private readonly string? _cliPath;

    public StLinkMemoryReader(string? cliPath = null)
    {
        _cliPath = cliPath ?? FindCli();
    }

    public string? CliPath => _cliPath;

    public async Task<uint> ReadWordAsync(uint address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cliPath) || !File.Exists(_cliPath))
            throw new FileNotFoundException("STM32_Programmer_CLI.exe를 찾을 수 없습니다.");

        var start = new ProcessStartInfo
        {
            FileName = _cliPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("port=SWD");
        start.ArgumentList.Add("mode=HOTPLUG");
        start.ArgumentList.Add("-r32");
        start.ArgumentList.Add($"0x{address:X8}");
        start.ArgumentList.Add("1");

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("STM32 Programmer CLI를 시작하지 못했습니다.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
            throw;
        }

        var output = (await stdoutTask.ConfigureAwait(false)) + Environment.NewLine
                   + (await stderrTask.ConfigureAwait(false));
        if (process.ExitCode != 0)
            throw new InvalidOperationException(LastMeaningfulLine(output) ?? "ST-LINK 연결에 실패했습니다.");

        var addressText = address.ToString("X8", CultureInfo.InvariantCulture);
        var match = Regex.Match(output,
            $@"0x{addressText}\s*:\s*(?:0x)?([0-9A-Fa-f]{{1,8}})",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            var generic = ReadValueRegex().Matches(output);
            if (generic.Count > 0) match = generic[^1];
        }

        if (!match.Success || !uint.TryParse(
                match.Groups[1].Value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var value))
            throw new InvalidOperationException($"GPIO 레지스터 0x{address:X8} 값을 읽지 못했습니다.");

        return value;
    }

    private static string? FindCli()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim(), "STM32_Programmer_CLI.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        var exactCandidates = new[]
        {
            @"C:\Program Files\STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe",
            @"C:\Program Files (x86)\STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe"
        };
        foreach (var candidate in exactCandidates)
            if (File.Exists(candidate)) return candidate;

        foreach (var root in new[]
                 {
                     @"C:\ST",
                     @"C:\Program Files\STMicroelectronics",
                     @"C:\Program Files (x86)\STMicroelectronics"
                 })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var match = Directory.EnumerateFiles(
                    root, "STM32_Programmer_CLI.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (match is not null) return match;
            }
            catch { }
        }

        return null;
    }

    private static string? LastMeaningfulLine(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
              .Select(line => line.Trim())
              .LastOrDefault(line => line.Length > 0);

    [GeneratedRegex(@"0x[0-9A-Fa-f]{8}\s*:\s*(?:0x)?([0-9A-Fa-f]{1,8})")]
    private static partial Regex ReadValueRegex();
}

public sealed record DioIdSwitchState(
    bool IdSet1High,
    bool IdSet2High,
    int Id,
    string Probe);

public interface IDioIdSwitchReader
{
    string? CliPath { get; }
    Task<DioIdSwitchState> ReadAsync(CancellationToken ct = default);
}

public sealed class StLinkDioIdSwitchReader : IDioIdSwitchReader
{
    private const uint GpioAIdrAddress = 0x50000010;
    private const uint GpioCIdrAddress = 0x50000810;
    private readonly StLinkMemoryReader _memory;

    public StLinkDioIdSwitchReader(StLinkMemoryReader? memory = null)
    {
        _memory = memory ?? new StLinkMemoryReader();
    }

    public string? CliPath => _memory.CliPath;

    public async Task<DioIdSwitchState> ReadAsync(CancellationToken ct = default)
    {
        var gpioA = await _memory.ReadWordAsync(GpioAIdrAddress, ct).ConfigureAwait(false);
        var gpioC = await _memory.ReadWordAsync(GpioCIdrAddress, ct).ConfigureAwait(false);
        var set1 = (gpioA & (1U << 0)) != 0;
        var set2 = (gpioC & (1U << 15)) != 0;
        var id = 1 + (set1 ? 1 : 0) + (set2 ? 2 : 0);
        return new DioIdSwitchState(set1, set2, id, Path.GetFileName(CliPath) ?? "STM32_Programmer_CLI");
    }
}

public sealed record CommRotaryState(
    int Position,
    string HexPosition,
    int RawHighNibble,
    string Probe);

public interface ICommRotaryReader
{
    string? CliPath { get; }
    Task<CommRotaryState> ReadAsync(CancellationToken ct = default);
}

public sealed class StLinkCommRotaryReader : ICommRotaryReader
{
    private const uint GpioCIdrAddress = 0x48000810;
    private readonly StLinkMemoryReader _memory;

    public StLinkCommRotaryReader(StLinkMemoryReader? memory = null)
    {
        _memory = memory ?? new StLinkMemoryReader();
    }

    public string? CliPath => _memory.CliPath;

    public async Task<CommRotaryState> ReadAsync(CancellationToken ct = default)
    {
        var gpioC = await _memory.ReadWordAsync(GpioCIdrAddress, ct).ConfigureAwait(false);
        var rawHigh = (int)(gpioC & 0x0F);
        var position = (~rawHigh) & 0x0F;
        return new CommRotaryState(
            position,
            position.ToString("X1", CultureInfo.InvariantCulture),
            rawHigh,
            Path.GetFileName(CliPath) ?? "STM32_Programmer_CLI");
    }
}
