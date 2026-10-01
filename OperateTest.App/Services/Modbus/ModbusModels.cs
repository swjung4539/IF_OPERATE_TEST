namespace OperateTest.App.Services.Modbus;

/// <summary>Modbus RTU function code. FC03/04/06/10에 이어 FC01/02/05/0F(Coil/Discrete Input 계열)를
/// 추가했다.</summary>
public static class ModbusFunctionCode
{
    public const byte ReadCoils = 0x01;
    public const byte ReadDiscreteInputs = 0x02;
    public const byte ReadHoldingRegisters = 0x03;
    public const byte ReadInputRegisters = 0x04;
    public const byte WriteSingleCoil = 0x05;
    public const byte WriteSingleRegister = 0x06;
    public const byte WriteMultipleCoils = 0x0F;
    public const byte WriteMultipleRegisters = 0x10;
}

/// <summary>FC03/FC04 요청 성공 결과.</summary>
public sealed record ModbusReadResult(byte SlaveId, byte FunctionCode, ushort StartAddress, ushort[] Registers, byte[] RawResponse);

/// <summary>FC01/FC02 요청 성공 결과. Bit 순서는 첫 코일/입력이 응답 데이터의 첫 바이트 LSB(비트0)에
/// 오는 표준 Modbus 순서를 따른다(IF_main의 <c>a_modbus_master.c</c>/<c>a_plc_group_map.c</c>에서
/// 확인한 <c>1U &lt;&lt; (offset % 8)</c> 패킹과 동일).</summary>
public sealed record ModbusBitReadResult(byte SlaveId, byte FunctionCode, ushort StartAddress, bool[] Bits, byte[] RawResponse);

/// <summary>FC06/FC10/FC05/FC0F 요청 성공 결과(echo 검증까지 통과한 상태). FC05/FC06은
/// <c>QuantityOrValue</c>에 실제 값(레지스터 값 또는 코일의 경우 0xFF00/0x0000)을,
/// FC0F/FC10은 수량을 담는다.</summary>
public sealed record ModbusWriteResult(byte SlaveId, byte FunctionCode, ushort Address, ushort QuantityOrValue, byte[] RawResponse);

/// <summary>Transaction 로그/Raw 표시에 쓰이는 결과 분류. COMMUNICATION_JUDGMENT_RULES.md 1절의
/// 5가지 응답 상태(정상/Exception/무응답/Timeout/CRC오류)에 Mismatch(Slave ID/Function/Byte Count/
/// Write Echo 검증 실패)를 더한 것이다.</summary>
public enum ModbusTransactionResult
{
    Success,
    Exception,
    Timeout,
    CrcError,
    Mismatch
}

/// <summary>한 번의 요청-응답 왕복을 그대로 기록한 Transaction. Raw 표시(hex 문자열)와 판정 결과를
/// 함께 담는다.</summary>
/// <summary><paramref name="ResponseTimeMs"/>는 요청 송신 시작부터 응답(또는 timeout 확정)까지 걸린
/// 실측 시간이다(OPEN_ITEMS_AUDIT.md OI-24 — 기존에는 typed 필드가 없어 통합 로그에 null로 남았다).
/// 트레일링 optional 필드라 기존 positional 생성 호출은 그대로 컴파일된다.</summary>
public sealed record ModbusTransaction(
    DateTime Timestamp,
    byte SlaveId,
    byte FunctionCode,
    string RequestHex,
    string? ResponseHex,
    ModbusTransactionResult Result,
    string Detail,
    double? ResponseTimeMs = null);
