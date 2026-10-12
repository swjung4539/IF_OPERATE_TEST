using OperateTest.App.Models;
using OperateTest.App.Services.Gst;
using OperateTest.App.Services.ModbusSlave;
using OperateTest.App.Services.Serial;

namespace OperateTest.App.Services.CommInspection;

public enum InspectionVerdict { NotRun, Running, Pass, Fail }

public sealed record CommPathResult(
    string ChannelKey,
    string DisplayName,
    InspectionVerdict Verdict,
    string Detail);

public interface ICommInspectionService
{
    Task<IReadOnlyList<CommPathResult>> RunAllAsync(
        IProgress<CommPathResult>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<CommPathResult>> RunSelectedAsync(
        IReadOnlyCollection<string> channelKeys,
        IProgress<CommPathResult>? progress = null,
        CancellationToken ct = default);
}

public sealed class CommInspectionService : ICommInspectionService
{
    private const byte StatusGroupId = 4;
    private const ushort StatusAddress = 163;
    private const int RequestTimeoutMs = 3000;
    private const int RetryCount = 3;

    private readonly ISerialConnectionManager _connections;
    private readonly IInspectionProtocolServices _protocols;

    public CommInspectionService(
        ISerialConnectionManager connections,
        IInspectionProtocolServices protocols)
    {
        _connections = connections;
        _protocols = protocols;
    }

    public Task<IReadOnlyList<CommPathResult>> RunAllAsync(
        IProgress<CommPathResult>? progress = null,
        CancellationToken ct = default) =>
        RunSelectedAsync(ChannelDefinitions.CommChannels.Select(x => x.Key).ToArray(), progress, ct);

    public async Task<IReadOnlyList<CommPathResult>> RunSelectedAsync(
        IReadOnlyCollection<string> channelKeys,
        IProgress<CommPathResult>? progress = null,
        CancellationToken ct = default)
    {
        var selected = channelKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = ChannelDefinitions.CommChannels
            .Where(definition => selected.Contains(definition.Key))
            .ToArray();

        foreach (var definition in definitions)
            progress?.Report(new CommPathResult(
                definition.Key, definition.DisplayName,
                InspectionVerdict.Running, "요청·응답 확인 중"));

        var tasks = definitions.Select(definition => RunPathAsync(definition, progress, ct));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<CommPathResult> RunPathAsync(
        SerialChannelDefinition definition,
        IProgress<CommPathResult>? progress,
        CancellationToken ct)
    {
        if (_connections.GetTransport(definition.Key).State != Serial.SerialConnectionState.Connected)
            return Report(definition, InspectionVerdict.Fail,
                "해당 COM 포트가 연결되지 않았습니다.",
                progress);

        try
        {
            return definition.Key switch
            {
                "comm-uart4" => await RunUart4Async(definition, progress, ct).ConfigureAwait(false),
                "comm-usart2" => await RunModbusSlaveAsync(
                    definition, ModbusSlaveBus.Usart2Heating, [20, 21, 22],
                    progress, ct).ConfigureAwait(false),
                "comm-usart3" => await RunGasAsync(definition, progress, ct).ConfigureAwait(false),
                "comm-usart1" => await RunGstAsync(definition, progress, ct).ConfigureAwait(false),
                _ => Report(definition, InspectionVerdict.Fail, "지원하지 않는 검사 경로입니다.", progress)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Report(definition, InspectionVerdict.Fail, Friendly(ex), progress);
        }
    }

    private async Task<CommPathResult> RunUart4Async(
        SerialChannelDefinition definition,
        IProgress<CommPathResult>? progress,
        CancellationToken ct)
    {
        var read = await ReadMainStatusAsync(ct).ConfigureAwait(false);
        return Report(definition, InspectionVerdict.Pass,
            $"MAIN UART4 정상 응답 · HR163=0x{read.Status:X4} · PLC HB {read.PlcHeartbeat}", progress);
    }

    private async Task<(ushort Status, ushort PlcHeartbeat)> ReadMainStatusAsync(CancellationToken ct)
    {
        var master = _protocols.GetModbusMaster(ChannelDefinitions.Uart4.Key);
        Exception? last = null;

        for (var attempt = 1; attempt <= RetryCount; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var read = await master.ReadHoldingRegistersAsync(
                    StatusGroupId, StatusAddress, 2, 1000, ct).ConfigureAwait(false);
                return (read.Registers[0], read.Registers[1]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException($"MAIN UART4 응답 없음: {Friendly(last)}");
    }

    private Task<CommPathResult> RunModbusSlaveAsync(
        SerialChannelDefinition definition,
        ModbusSlaveBus bus,
        IReadOnlyList<byte> ids,
        IProgress<CommPathResult>? progress,
        CancellationToken ct)
    {
        var stores = ids.Select(id => ModbusSlaveRegisterStore.CreateDefaultFor(bus, id)).ToArray();
        return RunModbusBusAsync(definition, stores, ids, progress, ct);
    }

    private Task<CommPathResult> RunGasAsync(
        SerialChannelDefinition definition,
        IProgress<CommPathResult>? progress,
        CancellationToken ct)
    {
        var stores = new[]
        {
            ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart3Gas, 30, GasDetectorProfile.Std),
            ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart3Gas, 40, GasDetectorProfile.Std),
            ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart3Gas, 50, GasDetectorProfile.Tft)
        };
        return RunModbusBusAsync(definition, stores, [30, 40, 50], progress, ct);
    }

    private async Task<CommPathResult> RunModbusBusAsync(
        SerialChannelDefinition definition,
        ModbusSlaveRegisterStore[] stores,
        IReadOnlyCollection<byte> expectedIds,
        IProgress<CommPathResult>? progress,
        CancellationToken ct)
    {
        var emulator = _protocols.CreateModbusSlaveEmulator(definition.Key, stores[0], stores);
        var pending = expectedIds.ToHashSet();
        var observed = new List<byte>();
        Exception? last = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (pending.Count > 0 && DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var remainingMs = (int)Math.Max(1,
                Math.Min(RequestTimeoutMs, (deadline - DateTime.UtcNow).TotalMilliseconds));
            try
            {
                var exchange = await emulator.ServiceNextBusRequestAsync(
                    remainingMs, ct).ConfigureAwait(false);
                if (exchange.Result != ModbusSlaveResponseResult.Success)
                {
                    last = new InvalidOperationException(exchange.Detail);
                    continue;
                }

                var slaveId = ParseSlaveId(exchange.RequestHex);
                if (!slaveId.HasValue || !expectedIds.Contains(slaveId.Value))
                {
                    last = new InvalidOperationException(
                        $"등록되지 않은 Slave 요청 수신: {exchange.RequestHex ?? "(없음)"}");
                    continue;
                }

                if (pending.Remove(slaveId.Value))
                    observed.Add(slaveId.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        if (pending.Count == 0)
        {
            var ids = string.Join(", ", observed.OrderBy(id => id));
            return Report(definition, InspectionVerdict.Pass,
                $"Slave ID {ids} 요청 수신 및 정상 응답 완료", progress);
        }

        var completed = observed.Count == 0
            ? "없음"
            : string.Join(", ", observed.OrderBy(id => id));
        var missing = string.Join(", ", pending.OrderBy(id => id));
        return Report(definition, InspectionVerdict.Fail,
            $"확인 ID: {completed} · 미확인 ID: {missing} · {Friendly(last)}", progress);
    }
    private async Task<CommPathResult> RunGstAsync(
        SerialChannelDefinition definition,
        IProgress<CommPathResult>? progress,
        CancellationToken ct)
    {
        var peer = _protocols.GetGstPeer(definition.Key);
        Exception? last = null;

        for (var attempt = 1; attempt <= RetryCount; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var frame = await peer.ReceiveFromControllerAsync(RequestTimeoutMs, ct).ConfigureAwait(false);
                await peer.SendStatusAsync(new ushort[41], ct).ConfigureAwait(false);
                return Report(definition, InspectionVerdict.Pass,
                    $"MAIN GST 프레임 수신 및 상태 응답 완료 (FC=0x{frame.FunctionCode:X2})", progress);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        return Report(definition, InspectionVerdict.Fail,
            $"GST 정상 프레임을 확인하지 못했습니다: {Friendly(last)}", progress);
    }

    private static CommPathResult Report(
        SerialChannelDefinition definition,
        InspectionVerdict verdict,
        string detail,
        IProgress<CommPathResult>? progress)
    {
        var result = new CommPathResult(definition.Key, definition.DisplayName, verdict, detail);
        progress?.Report(result);
        return result;
    }

    private static byte? ParseSlaveId(string? requestHex)
    {
        var first = requestHex?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return byte.TryParse(first, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? value
            : null;
    }
    private static string Friendly(Exception? ex) => ex switch
    {
        null => "응답 없음",
        SerialPortTimeoutException => "응답 시간 초과",
        TimeoutException => "응답 시간 초과",
        _ => ex.Message
    };
}
