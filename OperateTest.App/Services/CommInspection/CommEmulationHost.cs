using System.Collections.Concurrent;
using OperateTest.App.Models;
using OperateTest.App.Services.ModbusSlave;
using OperateTest.App.Services.Serial;

namespace OperateTest.App.Services.CommInspection;

public sealed record CommHostStatus(string Key, bool Healthy, string Detail, DateTime Timestamp);

public interface ICommEmulationHost
{
    bool IsRunning { get; }
    bool FaultTargetSuppressed { get; }
    event EventHandler<CommHostStatus>? StatusChanged;
    Task StartAsync(CancellationToken ct = default);
    Task PrepareNormalAsync(CancellationToken ct = default);
    Task InduceSlaveFaultAsync(CancellationToken ct = default);
    Task RestoreNormalAsync(CancellationToken ct = default);
    Task StopAsync(bool restoreNormal = true);
}

public sealed class CommEmulationHost : ICommEmulationHost
{
    private static readonly string[] RequiredHealthKeys =
        ["uart4", "dio", "heating-20", "heating-21", "heating-22", "gas-30", "gas-40", "gas-50", "gst"];
    private readonly ISerialConnectionManager _connections;
    private readonly IInspectionProtocolServices _protocols;
    private readonly ConcurrentDictionary<string, DateTime> _lastSuccess = new();
    private readonly List<Task> _workers = [];
    private readonly object _gate = new();
    private CancellationTokenSource? _hostCts;
    private volatile bool _suppressHeating20;
    private DateTime _lastSuppressedAt;

    public CommEmulationHost(ISerialConnectionManager connections, IInspectionProtocolServices protocols)
    {
        _connections = connections;
        _protocols = protocols;
    }

    public bool IsRunning => _hostCts is { IsCancellationRequested: false };
    public bool FaultTargetSuppressed => _suppressHeating20;
    public event EventHandler<CommHostStatus>? StatusChanged;

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (IsRunning) return Task.CompletedTask;
            var disconnected = ChannelDefinitions.CommPortChannels
                .Where(x => !_connections.Get(x.Key).IsConnected).Select(x => x.DisplayName).ToArray();
            if (disconnected.Length > 0)
                throw new InvalidOperationException($"먼저 COMM 포트를 연결하세요: {string.Join(", ", disconnected)}");

            _lastSuccess.Clear();
            _workers.Clear();
            _suppressHeating20 = false;
            ct.ThrowIfCancellationRequested();
            _hostCts = new CancellationTokenSource();
            var token = _hostCts.Token;
            _workers.Add(RunUart4Async(token));
            _workers.Add(RunModbusBusAsync(ChannelDefinitions.Heating.Key, ModbusSlaveBus.Usart2Heating,
                [ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart2Heating, 20),
                 ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart2Heating, 21),
                 ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart2Heating, 22)], "heating", token));
            _workers.Add(RunModbusBusAsync(ChannelDefinitions.Gas.Key, ModbusSlaveBus.Usart3Gas,
                [ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart3Gas, 30, GasDetectorProfile.Std),
                 ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart3Gas, 40, GasDetectorProfile.Std),
                 ModbusSlaveRegisterStore.CreateDefaultFor(ModbusSlaveBus.Usart3Gas, 50, GasDetectorProfile.Tft)], "gas", token));
            _workers.Add(RunGstAsync(token));
        }
        return Task.CompletedTask;
    }

    public async Task PrepareNormalAsync(CancellationToken ct = default)
    {
        await StartAsync(ct).ConfigureAwait(false);
        await WaitUntilHealthyAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        await Task.Delay(2300, ct).ConfigureAwait(false);
        if (!AreAllLinksRecent(TimeSpan.FromMilliseconds(2100)))
            throw new InvalidOperationException("정상 통신이 2초 동안 유지되지 않았습니다.");
    }

    public async Task InduceSlaveFaultAsync(CancellationToken ct = default)
    {
        if (!IsRunning) throw new InvalidOperationException("먼저 정상 통신 준비를 실행하세요.");
        _lastSuppressedAt = DateTime.MinValue;
        _suppressHeating20 = true;
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (_lastSuppressedAt == DateTime.MinValue)
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException("Heating ID20 요청을 관측하지 못했습니다.");
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        await Task.Delay(2300, ct).ConfigureAwait(false);
    }

    public async Task RestoreNormalAsync(CancellationToken ct = default)
    {
        if (!IsRunning) throw new InvalidOperationException("통신 에뮬레이터가 실행 중이 아닙니다.");
        var resumedAt = DateTime.UtcNow;
        _suppressHeating20 = false;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!_lastSuccess.TryGetValue("heating-20", out var at) || at < resumedAt)
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException("Heating ID20 정상 응답 복구를 확인하지 못했습니다.");
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        await Task.Delay(2300, ct).ConfigureAwait(false);
        if (!AreAllLinksRecent(TimeSpan.FromMilliseconds(2100)))
            throw new InvalidOperationException("전체 정상 통신 복구가 유지되지 않았습니다.");
    }

    public async Task StopAsync(bool restoreNormal = true)
    {
        CancellationTokenSource? cts;
        Task[] workers;
        lock (_gate) { cts = _hostCts; workers = _workers.ToArray(); }
        if (cts is null) return;
        _suppressHeating20 = false;
        cts.Cancel();
        try { await Task.WhenAll(workers).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { }
        lock (_gate)
        {
            if (ReferenceEquals(_hostCts, cts))
            {
                _workers.Clear();
                _hostCts.Dispose();
                _hostCts = null;
            }
        }
    }

    private async Task RunUart4Async(CancellationToken ct)
    {
        var master = _protocols.GetModbusMaster(ChannelDefinitions.Uart4.Key);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var read = await master.ReadHoldingRegistersAsync(4, 163, 1, 800, ct).ConfigureAwait(false);
                MarkSuccess("uart4", $"UART4 MAIN 응답 정상 · HR163=0x{read.Registers[0]:X4}");
                if ((read.Registers[0] & 1) != 0) MarkSuccess("dio", "DIO 내부 통신 정상");
                else MarkError("dio", "DIO 내부 통신 이상");
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { MarkError("uart4", ex.Message); await DelayAsync(ct).ConfigureAwait(false); }
        }
    }

    private async Task RunModbusBusAsync(string channelKey, ModbusSlaveBus bus,
        ModbusSlaveRegisterStore[] stores, string prefix, CancellationToken ct)
    {
        var emulator = _protocols.CreateModbusSlaveEmulator(channelKey, stores[0], stores);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var suppressed = bus == ModbusSlaveBus.Usart2Heating && _suppressHeating20 ? (byte?)20 : null;
                var exchange = await emulator.ServiceNextBusRequestAsync(1200, suppressed, ct).ConfigureAwait(false);
                var id = ParseSlaveId(exchange.RequestHex);
                if (exchange.Result == ModbusSlaveResponseResult.Success && id.HasValue)
                    MarkSuccess($"{prefix}-{id}", $"Slave ID {id} 정상 응답");
                else if (exchange.Result == ModbusSlaveResponseResult.FaultInjected && id == 20)
                {
                    _lastSuppressedAt = DateTime.UtcNow;
                    MarkError("heating-20", "Heating ID20 응답 차단 중");
                }
                else if (exchange.Result != ModbusSlaveResponseResult.NoRequestReceived)
                    MarkError($"{prefix}-{id?.ToString() ?? "?"}", exchange.Detail);
            }
            catch (OperationCanceledException) { return; }
            catch (SerialPortTimeoutException) { MarkError(prefix, "MAIN 요청 대기 중"); }
            catch (Exception ex) { MarkError(prefix, ex.Message); await DelayAsync(ct).ConfigureAwait(false); }
        }
    }

    private async Task RunGstAsync(CancellationToken ct)
    {
        var peer = _protocols.GetGstPeer(ChannelDefinitions.Backup.Key);
        var status = new ushort[41];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await peer.ReceiveFromControllerAsync(1200, ct).ConfigureAwait(false);
                await peer.SendStatusAsync(status, ct).ConfigureAwait(false);
                MarkSuccess("gst", "Backup GST 정상 응답");
            }
            catch (OperationCanceledException) { return; }
            catch (SerialPortTimeoutException) { MarkError("gst", "MAIN GST 프레임 대기 중"); }
            catch (Exception ex) { MarkError("gst", ex.Message); await DelayAsync(ct).ConfigureAwait(false); }
        }
    }

    private async Task WaitUntilHealthyAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!AreAllLinksRecent(TimeSpan.FromSeconds(2)))
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
            {
                var missing = RequiredHealthKeys.Where(key =>
                    !_lastSuccess.TryGetValue(key, out var at) || DateTime.UtcNow - at > TimeSpan.FromSeconds(2));
                throw new InvalidOperationException($"정상 통신을 확인하지 못했습니다: {string.Join(", ", missing)}");
            }
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    private bool AreAllLinksRecent(TimeSpan age)
    {
        var now = DateTime.UtcNow;
        return RequiredHealthKeys.All(key => _lastSuccess.TryGetValue(key, out var at) && now - at <= age);
    }

    private void MarkSuccess(string key, string detail)
    {
        _lastSuccess[key] = DateTime.UtcNow;
        StatusChanged?.Invoke(this, new CommHostStatus(key, true, detail, DateTime.Now));
    }

    private void MarkError(string key, string detail) =>
        StatusChanged?.Invoke(this, new CommHostStatus(key, false, detail, DateTime.Now));

    private static async Task DelayAsync(CancellationToken ct)
    {
        try { await Task.Delay(200, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private static byte? ParseSlaveId(string? hex)
    {
        var first = hex?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return byte.TryParse(first, System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : null;
    }
}
