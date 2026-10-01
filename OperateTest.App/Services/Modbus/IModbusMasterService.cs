namespace OperateTest.App.Services.Modbus;

/// <summary>
/// Modbus RTU Master 프로토콜 계층. Serial 계층(<see cref="OperateTest.App.Services.Serial.ISerialPortService"/>)
/// 위에서 프레임 생성/CRC/송수신/응답 검증/Transaction 기록만 담당한다(COMMUNICATION_IMPLEMENTATION_RULES.md
/// 3절 계층 분리). FC03/FC04/FC06/FC10에 이어 FC01/FC02/FC05/FC0F(Coil/Discrete Input 계열)를
/// 추가로 지원한다. 요청 큐/자동 검사는 <see cref="IModbusQueueTestService"/>가 별도로 담당하고,
/// Modbus Slave 기능은 포함하지 않는다. 연결/해제는 이 인터페이스의 책임이 아니며, 호출 전에
/// Serial 계층이 이미 연결되어 있어야 한다.
/// </summary>
public interface IModbusMasterService
{
    /// <summary>지금까지 수행한 모든 요청-응답 왕복 기록(성공/실패 모두 포함).</summary>
    IReadOnlyList<ModbusTransaction> Transactions { get; }

    /// <summary>Transaction이 추가될 때마다 발생(Raw 표시/로그 화면 갱신용).</summary>
    event EventHandler<ModbusTransaction>? TransactionRecorded;

    /// <summary>FC01: Read Coils.</summary>
    Task<ModbusBitReadResult> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct = default);

    /// <summary>FC02: Read Discrete Inputs.</summary>
    Task<ModbusBitReadResult> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct = default);

    /// <summary>FC03: Read Holding Registers.</summary>
    Task<ModbusReadResult> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct = default);

    /// <summary>FC04: Read Input Registers.</summary>
    Task<ModbusReadResult> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct = default);

    /// <summary>FC05: Write Single Coil.</summary>
    Task<ModbusWriteResult> WriteSingleCoilAsync(byte slaveId, ushort address, bool value, int timeoutMs, CancellationToken ct = default);

    /// <summary>FC06: Write Single Register.</summary>
    Task<ModbusWriteResult> WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, int timeoutMs, CancellationToken ct = default);

    /// <summary>FC0F: Write Multiple Coils.</summary>
    Task<ModbusWriteResult> WriteMultipleCoilsAsync(byte slaveId, ushort startAddress, bool[] values, int timeoutMs, CancellationToken ct = default);

    /// <summary>FC10: Write Multiple Registers.</summary>
    Task<ModbusWriteResult> WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, int timeoutMs, CancellationToken ct = default);

    void ClearTransactions();
}
