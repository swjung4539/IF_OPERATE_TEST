using System.IO.Ports;

namespace OperateTest.App.Services.Serial;

/// <summary>Serial 계층의 연결 상태. Modbus/GST 등 상위 프로토콜과는 무관하게 포트 자체의 상태만 표현한다.</summary>
public enum SerialConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>포트 오픈에 필요한 물리 계층 설정. COMMUNICATION_SPEC.md 2절의 포트별 값을 그대로 전달하면 된다.</summary>
public sealed record SerialPortOptions(
    string PortName,
    int BaudRate,
    int DataBits = 8,
    Parity Parity = Parity.None,
    StopBits StopBits = StopBits.One);

/// <summary>
/// COM 포트 검색, 연결/해제, 비동기 송수신, Timeout, 취소, 재연결, 케이블 분리 감지만 담당하는
/// 최하위 Serial 계층 인터페이스. Modbus/GST 프레임 구조나 CRC는 이 계층이 알지 못하며,
/// byte[] 원시 송수신만 제공한다(COMMUNICATION_IMPLEMENTATION_RULES.md 3절 계층 분리 원칙).
/// UI 코드는 이 인터페이스만 참조하고 System.IO.Ports.SerialPort를 직접 사용하지 않는다.
/// </summary>
public interface ISerialPortService : IDisposable
{
    SerialConnectionState State { get; }
    SerialPortOptions? CurrentOptions { get; }

    event EventHandler<SerialConnectionState>? StateChanged;

    /// <summary>연결된 상태에서 송수신 중 케이블 분리 등으로 포트가 예기치 않게 끊어졌을 때 발생한다.
    /// 인자는 사람이 읽을 수 있는 원인 설명이다. 구독자가 UI 스레드 마샬링을 책임진다.</summary>
    event EventHandler<string>? PortLost;

    /// <summary>현재 PC에 존재하는 COM 포트 이름 목록을 조회한다(COM 검색).</summary>
    IReadOnlyList<string> GetAvailablePortNames();

    /// <summary>현재 PC에 존재하는 COM 포트를 실제 포트명과 Friendly Name(장치 설명)을 함께 조회한다.
    /// Friendly Name 조회에 실패한 포트는 <see cref="SerialPortInfo.DisplayName"/>이 포트명과 같은
    /// 값으로 채워진다. 내부 연결(<see cref="ConnectAsync"/>)에는 항상
    /// <see cref="SerialPortInfo.PortName"/>을 사용해야 한다.</summary>
    IReadOnlyList<SerialPortInfo> GetAvailablePorts();

    /// <summary>포트를 연다. 이미 다른 프로세스가 점유 중이면 <see cref="SerialPortBusyException"/>,
    /// 존재하지 않는 포트면 <see cref="SerialPortNotFoundException"/>을 던진다.</summary>
    Task ConnectAsync(SerialPortOptions options, CancellationToken ct = default);

    /// <summary>포트를 닫고 운영체제 핸들을 해제한다(포트 해제).</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>마지막으로 연결에 성공했던 설정으로 닫힌 포트를 다시 연다. 아직 한 번도 연결한 적이
    /// 없으면 false를 반환한다.</summary>
    Task<bool> ReconnectAsync(CancellationToken ct = default);

    /// <summary>data를 그대로 전송한다. timeoutMs 안에 전송이 끝나지 않으면
    /// <see cref="SerialPortTimeoutException"/>, 케이블 분리 등으로 전송 자체가 실패하면
    /// <see cref="SerialPortCableLostException"/>을 던진다.</summary>
    Task SendAsync(byte[] data, int timeoutMs, CancellationToken ct = default);

    /// <summary>최대 maxLength 바이트까지, timeoutMs 안에 수신된 만큼만 반환한다(프레임 조립은
    /// 상위 프로토콜 계층의 책임). timeoutMs 안에 한 바이트도 오지 않으면
    /// <see cref="SerialPortTimeoutException"/>을 던진다.</summary>
    Task<byte[]> ReceiveAsync(int maxLength, int timeoutMs, CancellationToken ct = default);

    /// <summary>REQUIREMENTS 5절 — UART4 첫 실행 ERROR 원인 수정. OS 드라이버 수신 버퍼에 아직
    /// 읽지 않은 채 남아있을 수 있는 바이트(예: 짧은 timeout으로 포기한 연결 확인 프로브의 응답이
    /// 그보다 늦게 도착해 아무도 읽지 않은 경우)를 명시적으로 비운다. 연결돼 있지 않으면 아무 일도
    /// 하지 않는다. <see cref="ConnectAsync"/>가 포트를 여는 시점의 잔여 바이트를 이미 비우지만,
    /// 그 이후(연결 확인 프로브 등) 새로 쌓인 잔여 바이트는 별도로 비워야 한다.</summary>
    Task DiscardBuffersAsync(CancellationToken ct = default);
}
