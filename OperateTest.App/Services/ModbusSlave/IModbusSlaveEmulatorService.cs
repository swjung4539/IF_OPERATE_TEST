namespace OperateTest.App.Services.ModbusSlave;

public interface IModbusSlaveEmulatorService
{
    IReadOnlyList<ModbusSlaveExchange> Exchanges { get; }
    event EventHandler<ModbusSlaveExchange>? ExchangeRecorded;
    void ClearExchanges();

    Task<ModbusSlaveExchange> ServiceNextBusRequestAsync(
        int timeoutMs,
        CancellationToken ct = default);

    Task<ModbusSlaveExchange> ServiceNextBusRequestAsync(
        int timeoutMs,
        byte? suppressedSlaveId,
        CancellationToken ct = default);

    Task<ModbusSlaveExchange> ServiceNextRequestAsync(
        int timeoutMs,
        CancellationToken ct = default);

    Task<ModbusSlaveExchange> ServiceNextRequestWithFaultAsync(
        ModbusSlaveFaultKind faultKind,
        int waitTimeoutMs,
        int delayMs,
        CancellationToken ct = default);
}
