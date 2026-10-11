using OperateTest.App.Services.Modbus;
using OperateTest.App.Services.Serial;

namespace OperateTest.App.Services.DioInspection;

public enum DioChannelVerdict { NotRun, Running, Pass, Fail }

public sealed record DioLoopbackProgress(
    int Channel,
    DioChannelVerdict Verdict,
    ushort Baseline,
    ushort Current,
    ushort ExpectedChange,
    ushort ActualChange,
    string Detail,
    int Completed,
    int Total);

public sealed record DioLoopbackResult(
    byte SlaveId,
    ushort Baseline,
    IReadOnlyList<DioLoopbackProgress> Channels,
    bool AllOnPassed,
    bool OutputsCleared,
    bool Passed,
    string Detail);

public sealed record DioOutputSafetyResult(
    bool CommandSent,
    bool Verified,
    ushort? CurrentInput,
    string Detail);

public interface IDioLoopbackService
{
    Task<byte> FindSlaveIdAsync(CancellationToken ct = default);
    Task SetAllOutputsOffAsync(byte dioSlot, CancellationToken ct = default);
    Task<DioOutputSafetyResult> EnsureAllOutputsOffAsync(
        byte dioSlot,
        ushort? expectedInput,
        CancellationToken ct = default);
    Task<DioLoopbackResult> RunAsync(
        IProgress<DioLoopbackProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed class DioLoopbackService : IDioLoopbackService
{
    private const byte ReadDioGroupId = 1;
    private const byte WriteDioGroupId = 5;
    private const byte StatusGroupId = 4;
    private const ushort StatusAddress = 163;
    private const byte DefaultDioSlot = 1;
    private const int TimeoutMs = 1000;
    private readonly IModbusMasterService _master;

    public DioLoopbackService(IInspectionProtocolServices protocols)
    {
        _master = protocols.GetModbusMaster(Models.ChannelDefinitions.Uart4.Key);
    }

    public async Task<byte> FindSlaveIdAsync(CancellationToken ct = default)
    {
        var status = await _master.ReadHoldingRegistersAsync(
            StatusGroupId, StatusAddress, 1, TimeoutMs, ct).ConfigureAwait(false);

        if ((status.Registers[0] & 0x0001) == 0)
            throw new InvalidOperationException(
                "MAIN의 DIO 내부 통신 상태가 OFF입니다. DIO 전원과 MAIN↔DIO 연결을 확인하세요.");

        await _master.ReadHoldingRegistersAsync(
            ReadDioGroupId, ReadAddress(DefaultDioSlot), 1, TimeoutMs, ct).ConfigureAwait(false);
        return DefaultDioSlot;
    }

    public async Task SetAllOutputsOffAsync(byte dioSlot, CancellationToken ct = default)
    {
        await _master.WriteMultipleRegistersAsync(
            WriteDioGroupId, WriteAddress(dioSlot), [0x0000], TimeoutMs, ct).ConfigureAwait(false);
        await Task.Delay(80, ct).ConfigureAwait(false);
    }

    public async Task<DioOutputSafetyResult> EnsureAllOutputsOffAsync(
        byte dioSlot,
        ushort? expectedInput,
        CancellationToken ct = default)
    {
        await SetAllOutputsOffAsync(dioSlot, ct).ConfigureAwait(false);
        if (!expectedInput.HasValue)
            return new DioOutputSafetyResult(true, false, null, "DO 전체 OFF 명령을 전송했지만 기준 DI 값이 없어 복귀 여부는 확인하지 못했습니다.");

        var current = await ReadStableInputAsync(dioSlot, ct).ConfigureAwait(false);
        var verified = current == expectedInput.Value;
        return new DioOutputSafetyResult(
            true,
            verified,
            current,
            verified
                ? "DO 전체 OFF 후 DI가 초기 상태로 복귀했습니다."
                : $"DO 전체 OFF 후 DI가 초기 상태로 복귀하지 않았습니다. (기준 0x{expectedInput:X4}, 현재 0x{current:X4})");
    }

    public async Task<DioLoopbackResult> RunAsync(
        IProgress<DioLoopbackProgress>? progress = null,
        CancellationToken ct = default)
    {
        var slot = await FindSlaveIdAsync(ct).ConfigureAwait(false);
        var results = new List<DioLoopbackProgress>();
        var outputsCleared = false;
        ushort baseline = 0;
        var allOnPassed = false;

        try
        {
            await SetAllOutputsOffAsync(slot, ct).ConfigureAwait(false);
            baseline = await ReadStableInputAsync(slot, ct).ConfigureAwait(false);

            for (var channel = 1; channel <= 16; channel++)
            {
                ct.ThrowIfCancellationRequested();
                var expected = (ushort)(1U << (channel - 1));
                progress?.Report(new DioLoopbackProgress(
                    channel, DioChannelVerdict.Running, baseline, baseline,
                    expected, 0, "검사 중", results.Count, 16));

                await WriteOutputAsync(slot, expected, ct).ConfigureAwait(false);
                await Task.Delay(100, ct).ConfigureAwait(false);
                var current = await ReadStableInputAsync(slot, ct).ConfigureAwait(false);
                var changed = (ushort)(current ^ baseline);
                var pass = changed == expected;

                await SetAllOutputsOffAsync(slot, ct).ConfigureAwait(false);
                var returned = await ReadStableInputAsync(slot, ct).ConfigureAwait(false);
                if (returned != baseline)
                    pass = false;

                var detail = BuildChannelDetail(pass, expected, changed, returned == baseline);
                var item = new DioLoopbackProgress(
                    channel,
                    pass ? DioChannelVerdict.Pass : DioChannelVerdict.Fail,
                    baseline, current, expected, changed, detail,
                    results.Count + 1, 16);
                results.Add(item);
                progress?.Report(item);
            }

            await WriteOutputAsync(slot, 0xFFFF, ct).ConfigureAwait(false);
            await Task.Delay(120, ct).ConfigureAwait(false);
            var allOnInput = await ReadStableInputAsync(slot, ct).ConfigureAwait(false);
            allOnPassed = (ushort)(allOnInput ^ baseline) == 0xFFFF;

            await SetAllOutputsOffAsync(slot, ct).ConfigureAwait(false);
            outputsCleared = await ReadStableInputAsync(slot, ct).ConfigureAwait(false) == baseline;

            var passed = results.All(x => x.Verdict == DioChannelVerdict.Pass)
                         && allOnPassed && outputsCleared;
            return new DioLoopbackResult(
                slot, baseline, results, allOnPassed, outputsCleared, passed,
                passed ? "16채널 루프백과 전체 ON/OFF 검사가 정상입니다."
                       : "실패 채널 또는 전체 ON/OFF 결과를 확인하세요.");
        }
        finally
        {
            try
            {
                await SetAllOutputsOffAsync(slot, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 연결이 끊어진 경우에는 화면에서 수동 출력 확인을 안내한다.
            }
        }
    }

    private Task WriteOutputAsync(byte dioSlot, ushort value, CancellationToken ct) =>
        _master.WriteMultipleRegistersAsync(
            WriteDioGroupId, WriteAddress(dioSlot), [value], TimeoutMs, ct);

    private async Task<ushort> ReadStableInputAsync(byte dioSlot, CancellationToken ct)
    {
        ushort? previous = null;
        var consecutive = 0;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var read = await _master.ReadHoldingRegistersAsync(
                ReadDioGroupId, ReadAddress(dioSlot), 1, TimeoutMs, ct).ConfigureAwait(false);
            var current = read.Registers[0];

            if (previous == current)
            {
                consecutive++;
                if (consecutive >= 2)
                    return current;
            }
            else
            {
                previous = current;
                consecutive = 1;
            }

            await Task.Delay(35, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException("DI 값이 안정되지 않았습니다.");
    }

    private static ushort ReadAddress(byte dioSlot) =>
        dioSlot == 2 ? (ushort)2 : (ushort)1;

    private static ushort WriteAddress(byte dioSlot) =>
        dioSlot == 2 ? (ushort)202 : (ushort)201;

    private static string BuildChannelDetail(bool pass, ushort expected, ushort actual, bool returned)
    {
        if (pass) return "정상";
        if (!returned) return "OFF 후 baseline으로 복귀하지 않았습니다.";
        if (actual == 0) return "DI 변화가 없습니다.";
        if ((actual & (actual - 1)) != 0) return $"여러 DI가 동시에 변했습니다. (0x{actual:X4})";
        return $"예상 0x{expected:X4}, 실제 0x{actual:X4}";
    }
}
