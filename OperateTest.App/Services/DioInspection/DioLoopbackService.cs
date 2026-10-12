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
    private const byte TargetDioId = 0;
    private const int TimeoutMs = 1000;
    private readonly IModbusMasterService _master;

    public DioLoopbackService(IInspectionProtocolServices protocols)
    {
        _master = protocols.GetModbusMaster(Models.ChannelDefinitions.Uart4.Key);
    }

    public async Task<byte> FindSlaveIdAsync(CancellationToken ct = default)
    {
        // The first MAIN DIO map entry belongs to the board configured as ID 0.
        // This proves that MAIN answers its PLC group map. It does not prove
        // that the downstream DIO board received a forwarded command.
        try
        {
            await _master.ReadHoldingRegistersAsync(
                ReadDioGroupId, ReadAddress(TargetDioId), 1, TimeoutMs, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "MAIN의 DIO 입력 맵(HR1) 응답이 없습니다. MAIN UART4 연결을 확인하세요.", ex);
        }
        return TargetDioId;
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

        var (current, verified) = await WaitForInputAsync(dioSlot, expectedInput.Value, 0, ct).ConfigureAwait(false);
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
                var (current, onSettled) = await WaitForInputAsync(slot, (ushort)(baseline ^ expected), expected, ct).ConfigureAwait(false);
                var changed = (ushort)(current ^ baseline);
                var pass = onSettled && changed == expected;

                await SetAllOutputsOffAsync(slot, ct).ConfigureAwait(false);
                var (returned, offSettled) = await WaitForInputAsync(slot, baseline, 0, ct).ConfigureAwait(false);
                if (!offSettled)
                    pass = false;

                var detail = BuildChannelDetail(pass, expected, changed, returned == baseline);
                var item = new DioLoopbackProgress(
                    channel,
                    pass ? DioChannelVerdict.Pass : DioChannelVerdict.Fail,
                    baseline, current, expected, changed, detail,
                    results.Count + 1, 16);
                results.Add(item);
                progress?.Report(item);
                if (channel == 1 && !onSettled && changed == 0)
                    return new DioLoopbackResult(slot, baseline, results, false, offSettled, false,
                        $"DO1 명령 후 DI1 변화가 없습니다. MAIN은 쓰기 ACK를 반환했지만 DIO 출력 반영은 확인되지 않았습니다. " +
                        $"DI 기준 0x{baseline:X4}, 현재 0x{current:X4}. MAIN↔DIO 연결과 DIO 물리 ID 0(통신 ID 1)을 확인하세요.");
            }

            await WriteOutputAsync(slot, 0xFFFF, ct).ConfigureAwait(false);
            var (_, allOnSettled) = await WaitForInputAsync(slot, (ushort)(baseline ^ 0xFFFF), 0xFFFF, ct).ConfigureAwait(false);
            allOnPassed = allOnSettled;

            await SetAllOutputsOffAsync(slot, ct).ConfigureAwait(false);
            var (_, allOffSettled) = await WaitForInputAsync(slot, baseline, 0, ct).ConfigureAwait(false);
            outputsCleared = allOffSettled;

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

    private async Task<(ushort Current, bool Matched)> WaitForInputAsync(byte dioSlot, ushort expected, ushort retryOutput, CancellationToken ct)
    {
        // MAIN acknowledges the PLC write before forwarding it to the DIO bus.
        // Reissue an unchanged output if the first forwarding attempt was dropped.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        var nextRetry = DateTime.UtcNow.AddMilliseconds(300);
        ushort current = 0;
        var matches = 0;
        do
        {
            ct.ThrowIfCancellationRequested();
            var read = await _master.ReadHoldingRegistersAsync(
                ReadDioGroupId, ReadAddress(dioSlot), 1, TimeoutMs, ct).ConfigureAwait(false);
            current = read.Registers[0];
            matches = current == expected ? matches + 1 : 0;
            if (matches >= 2) return (current, true);
            if (DateTime.UtcNow >= nextRetry)
            {
                await WriteOutputAsync(dioSlot, retryOutput, ct).ConfigureAwait(false);
                nextRetry = DateTime.UtcNow.AddMilliseconds(300);
            }
            await Task.Delay(40, ct).ConfigureAwait(false);
        } while (DateTime.UtcNow < deadline);
        return (current, false);
    }

    private static ushort ReadAddress(byte dioSlot) =>
        dioSlot == TargetDioId ? (ushort)1 : throw new ArgumentOutOfRangeException(nameof(dioSlot), "ID 0 보드만 검사할 수 있습니다.");

    private static ushort WriteAddress(byte dioSlot) =>
        dioSlot == TargetDioId ? (ushort)201 : throw new ArgumentOutOfRangeException(nameof(dioSlot), "ID 0 보드만 검사할 수 있습니다.");

    private static string BuildChannelDetail(bool pass, ushort expected, ushort actual, bool returned)
    {
        if (pass) return "정상";
        if (!returned) return "OFF 후 baseline으로 복귀하지 않았습니다.";
        if (actual == 0) return "DI 변화가 없습니다.";
        if ((actual & (actual - 1)) != 0) return $"여러 DI가 동시에 변했습니다. (0x{actual:X4})";
        return $"예상 0x{expected:X4}, 실제 0x{actual:X4}";
    }
}
