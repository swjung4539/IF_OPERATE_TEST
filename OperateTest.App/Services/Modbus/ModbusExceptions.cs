namespace OperateTest.App.Services.Modbus;

/// <summary>Modbus Master 프로토콜 계층에서 발생하는 예외의 공통 기반. Serial 계층 예외
/// (<see cref="OperateTest.App.Services.Serial.SerialLayerException"/>)와 구분되는, 프레임 해석/검증
/// 단계에서만 발생하는 예외다. COMMUNICATION_JUDGMENT_RULES.md 1절의 5가지 응답 상태 분류를
/// 코드 레벨에서 구분할 수 있도록 종류별 하위 클래스를 둔다.</summary>
public abstract class ModbusProtocolException : Exception
{
    public byte SlaveId { get; }
    public byte FunctionCode { get; }

    protected ModbusProtocolException(byte slaveId, byte functionCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        SlaveId = slaveId;
        FunctionCode = functionCode;
    }
}

/// <summary>지정한 시간 안에 응답이 전혀 오지 않았을 때(무응답 포함). Serial 계층의
/// <see cref="OperateTest.App.Services.Serial.SerialPortTimeoutException"/>을 Modbus 문맥(요청 slave/
/// function code)으로 감싼 것이다.</summary>
public sealed class ModbusTimeoutException : ModbusProtocolException
{
    public int TimeoutMs { get; }

    public ModbusTimeoutException(byte slaveId, byte functionCode, int timeoutMs, Exception? inner = null)
        : base(slaveId, functionCode,
            $"Slave {slaveId}, FC 0x{functionCode:X2} 요청에 {timeoutMs}ms 안에 응답이 없습니다(Timeout).", inner)
    {
        TimeoutMs = timeoutMs;
    }
}

/// <summary>슬레이브가 표준 Modbus Exception 응답(function code에 0x80을 OR한 프레임)으로 응답했을 때.</summary>
public sealed class ModbusExceptionResponseException : ModbusProtocolException
{
    public byte ExceptionCode { get; }

    public ModbusExceptionResponseException(byte slaveId, byte functionCode, byte exceptionCode)
        : base(slaveId, functionCode,
            $"Slave {slaveId}가 FC 0x{functionCode:X2} 요청에 Modbus Exception(code=0x{exceptionCode:X2})으로 응답했습니다.")
    {
        ExceptionCode = exceptionCode;
    }
}

/// <summary>응답 프레임의 CRC16이 맞지 않을 때.</summary>
public sealed class ModbusCrcMismatchException : ModbusProtocolException
{
    public ushort ExpectedCrc { get; }
    public ushort ActualCrc { get; }

    public ModbusCrcMismatchException(byte slaveId, byte functionCode, ushort expectedCrc, ushort actualCrc)
        : base(slaveId, functionCode,
            $"CRC 불일치: 프레임이 담고 있는 값 0x{actualCrc:X4}, 재계산 값 0x{expectedCrc:X4}")
    {
        ExpectedCrc = expectedCrc;
        ActualCrc = actualCrc;
    }
}

/// <summary>응답 프레임의 Slave ID가 요청과 다를 때.</summary>
public sealed class ModbusSlaveIdMismatchException : ModbusProtocolException
{
    public byte ActualSlaveId { get; }

    public ModbusSlaveIdMismatchException(byte requestedSlaveId, byte functionCode, byte actualSlaveId)
        : base(requestedSlaveId, functionCode,
            $"Slave ID 불일치: 요청 {requestedSlaveId}, 응답 {actualSlaveId}")
    {
        ActualSlaveId = actualSlaveId;
    }
}

/// <summary>응답 프레임의 Function Code가 요청(또는 Exception 형식)과 다를 때.</summary>
public sealed class ModbusFunctionCodeMismatchException : ModbusProtocolException
{
    public byte ActualFunctionCode { get; }

    public ModbusFunctionCodeMismatchException(byte slaveId, byte requestedFunctionCode, byte actualFunctionCode)
        : base(slaveId, requestedFunctionCode,
            $"Function Code 불일치: 요청 0x{requestedFunctionCode:X2}, 응답 0x{actualFunctionCode:X2}")
    {
        ActualFunctionCode = actualFunctionCode;
    }
}

/// <summary>FC03/FC04 응답의 Byte Count 필드가 요청한 수량과 맞지 않거나, 그로부터 계산되는 전체
/// 프레임 길이가 실제 수신 길이와 다를 때.</summary>
public sealed class ModbusByteCountMismatchException : ModbusProtocolException
{
    public ModbusByteCountMismatchException(byte slaveId, byte functionCode, string detail)
        : base(slaveId, functionCode, "Byte Count 불일치: " + detail)
    {
    }
}

/// <summary>FC06/FC10 응답이 요청을 그대로 반영(echo)해야 하는데 주소/값/수량/전체 길이가 다를 때.</summary>
public sealed class ModbusWriteEchoMismatchException : ModbusProtocolException
{
    public ModbusWriteEchoMismatchException(byte slaveId, byte functionCode, string detail)
        : base(slaveId, functionCode, "Write Echo 불일치: " + detail)
    {
    }
}
