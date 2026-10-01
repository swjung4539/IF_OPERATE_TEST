namespace OperateTest.App.Services.Serial;

/// <summary>Serial 계층에서 발생하는 예외의 공통 기반. COMMUNICATION_JUDGMENT_RULES.md 1절의
/// 무응답/Timeout 분류를 코드 레벨에서 구분할 수 있도록 종류별 하위 클래스를 둔다.</summary>
public abstract class SerialLayerException : Exception
{
    public string PortName { get; }

    protected SerialLayerException(string portName, string message, Exception? inner = null)
        : base(message, inner)
    {
        PortName = portName;
    }
}

/// <summary>지정한 이름의 COM 포트가 이 PC에 존재하지 않을 때(예: 뽑혀 있거나 오타).</summary>
public sealed class SerialPortNotFoundException : SerialLayerException
{
    public SerialPortNotFoundException(string portName, Exception? inner = null)
        : base(portName, $"포트 '{portName}'를 찾을 수 없습니다.", inner)
    {
    }
}

/// <summary>다른 프로세스(또는 이 프로세스의 다른 연결)가 이미 포트를 점유하고 있을 때.</summary>
public sealed class SerialPortBusyException : SerialLayerException
{
    public SerialPortBusyException(string portName, Exception? inner = null)
        : base(portName, $"포트 '{portName}'가 다른 프로그램에서 사용 중입니다.", inner)
    {
    }
}

/// <summary>송신/수신이 지정한 시간 안에 끝나지 않았을 때(취소가 아니라 순수 시간 초과).</summary>
public sealed class SerialPortTimeoutException : SerialLayerException
{
    public int TimeoutMs { get; }

    public SerialPortTimeoutException(string portName, int timeoutMs)
        : base(portName, $"포트 '{portName}' 응답이 {timeoutMs}ms 안에 오지 않았습니다(Timeout).")
    {
        TimeoutMs = timeoutMs;
    }
}

/// <summary>이 프로그램 안의 다른 채널(General Master/Slave/UART4/DIO/Heating/Gas/GST)이 이미
/// 같은 COM 포트 이름으로 연결되어 있을 때. OS 레벨 <see cref="SerialPortBusyException"/>과 달리
/// 어느 채널이 점유 중인지 이름으로 알려준다(COMMUNICATION_SCOPE_AUDIT.md "동일 COM 포트 중복
/// 점유 차단" 항목).</summary>
public sealed class SerialPortInUseByChannelException : SerialLayerException
{
    public string OwningChannel { get; }

    public SerialPortInUseByChannelException(string portName, string owningChannel)
        : base(portName, $"포트 '{portName}'는 이미 '{owningChannel}' 채널에서 사용 중입니다.")
    {
        OwningChannel = owningChannel;
    }
}

/// <summary>연결된 상태에서 송수신 도중 케이블 분리 등 물리적 원인으로 포트 자체가 끊어졌을 때.
/// 이 예외가 발생하면 서비스는 내부적으로 포트를 이미 Dispose하여 해제한 뒤 상태를 Failed로
/// 전환하고 <see cref="ISerialPortService.PortLost"/> 이벤트를 발생시킨다.</summary>
public sealed class SerialPortCableLostException : SerialLayerException
{
    public SerialPortCableLostException(string portName, Exception? inner = null)
        : base(portName, $"포트 '{portName}' 연결이 끊어졌습니다(케이블 분리 등).", inner)
    {
    }
}
