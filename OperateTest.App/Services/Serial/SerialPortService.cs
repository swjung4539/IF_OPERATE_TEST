using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace OperateTest.App.Services.Serial;

/// <summary>
/// System.IO.Ports.SerialPort를 감싸는 실제 Serial 계층 구현. 이 클래스가 SerialPort를 다루는
/// 유일한 위치이며(COMMUNICATION_IMPLEMENTATION_RULES.md 3절), 그 위의 프로토콜/실행/UI 계층은
/// 항상 <see cref="ISerialPortService"/> 인터페이스만 참조한다. Modbus 프레임 생성, CRC 계산,
/// 파싱은 이 클래스에 두지 않는다(byte[] 원시 송수신만 제공).
/// </summary>
public sealed class SerialPortService : ISerialPortService
{
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private SerialPort? _port;
    private SerialConnectionState _state = SerialConnectionState.Disconnected;

    public SerialConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            StateChanged?.Invoke(this, _state);
        }
    }

    public SerialPortOptions? CurrentOptions { get; private set; }

    public event EventHandler<SerialConnectionState>? StateChanged;
    public event EventHandler<string>? PortLost;

    public IReadOnlyList<string> GetAvailablePortNames()
    {
        return SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Win32_PnPEntity의 Name(예: "USB-SERIAL CH340 (COM3)")에서 포트명과 Friendly Name을
    /// 함께 조회한다. WMI 조회 자체가 실패하거나(드라이버/권한 문제 등) 특정 포트의 Friendly Name을
    /// 찾지 못하면 그 포트는 DisplayName이 PortName과 같은 값으로 채워진다 — 조회 실패가 COM 검색
    /// 자체의 실패로 번지지 않는다.</summary>
    public IReadOnlyList<SerialPortInfo> GetAvailablePorts()
    {
        var portNames = GetAvailablePortNames();
        var friendlyByPort = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    var name = item["Name"] as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var match = Regex.Match(name, @"\((COM\d+)\)\s*$");
                    if (!match.Success) continue;

                    var portName = match.Groups[1].Value;
                    var friendlyName = name[..match.Index].Trim();
                    friendlyByPort[portName] = string.IsNullOrWhiteSpace(friendlyName) ? name : friendlyName;
                }
            }
        }
        catch
        {
            // WMI 조회 실패(권한/환경 문제 등) - Friendly Name 없이 포트명만으로 계속 진행한다.
        }

        return portNames
            .Select(portName => friendlyByPort.TryGetValue(portName, out var friendly)
                ? new SerialPortInfo(portName, $"{portName} ({friendly})")
                : new SerialPortInfo(portName, portName))
            .ToList();
    }

    public async Task ConnectAsync(SerialPortOptions options, CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (State == SerialConnectionState.Connected)
                throw new InvalidOperationException("이미 연결된 상태에서는 다시 연결할 수 없습니다. 먼저 연결을 해제하세요.");

            State = SerialConnectionState.Connecting;

            var port = new SerialPort(options.PortName, options.BaudRate, options.Parity, options.DataBits, options.StopBits)
            {
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = SerialPort.InfiniteTimeout
            };

            try
            {
                // SerialPort.Open()은 동기 API라 스레드풀로 넘겨 UI/호출 스레드를 막지 않는다.
                await Task.Run(port.Open, ct).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex)
            {
                port.Dispose();
                State = SerialConnectionState.Failed;
                throw new SerialPortBusyException(options.PortName, ex);
            }
            catch (FileNotFoundException ex)
            {
                port.Dispose();
                State = SerialConnectionState.Failed;
                throw new SerialPortNotFoundException(options.PortName, ex);
            }
            catch (IOException ex)
            {
                // 일부 환경에서 존재하지 않는 포트가 FileNotFoundException 대신 IOException으로 온다.
                port.Dispose();
                State = SerialConnectionState.Failed;
                throw new SerialPortNotFoundException(options.PortName, ex);
            }
            catch (OperationCanceledException)
            {
                port.Dispose();
                State = SerialConnectionState.Disconnected;
                throw;
            }

            try
            {
                // 요구사항 8절 — 포트를 Open한 시점에 OS 드라이버 입력 버퍼에 이미 남아있을 수 있는
                // 이전 잡음(DTR/RTS 전환 중 상대 장치가 보낸 부트업 바이트, 이전 세션의 잔여 바이트 등)을
                // 첫 요청을 보내기 전에 반드시 비운다. 이걸 하지 않으면 Connect 직후 첫 SendAsync/
                // ReceiveAsync 왕복에서 그 잡음을 "응답"으로 그대로 읽어버려, 첫 번째 검사(예: UART4
                // Fault 케이스의 Baseline 확인)만 CRC/길이 불일치로 실패하고 재실행하면 버퍼가 이미
                // 비어 있어 정상 동작하는 증상이 재현됐다 — 재시도나 긴 Sleep으로 증상을 가리는 대신,
                // 수신 준비가 되기 전에 쌓였을 수 있는 데이터를 명시적으로 제거한다.
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
            }
            catch (IOException ex)
            {
                port.Dispose();
                State = SerialConnectionState.Failed;
                throw new SerialPortCableLostException(options.PortName, ex);
            }

            _port = port;
            CurrentOptions = options;
            State = SerialConnectionState.Connected;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ClosePortInternal();
            State = SerialConnectionState.Disconnected;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<bool> ReconnectAsync(CancellationToken ct = default)
    {
        var options = CurrentOptions;
        if (options is null) return false;

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            State = SerialConnectionState.Reconnecting;
            ClosePortInternal();
        }
        finally
        {
            _ioLock.Release();
        }

        try
        {
            await ConnectAsync(options, ct).ConfigureAwait(false);
            return true;
        }
        catch (SerialLayerException)
        {
            State = SerialConnectionState.Failed;
            return false;
        }
    }

    /// <summary>native COMMTIMEOUTS 기반 polling 간격. 실측 결과
    /// <c>SerialPort.BaseStream.ReadAsync/WriteAsync</c>에 CancellationToken을 직접 물리면 실제
    /// 하드웨어 포트(COM1 등)에서 취소가 전혀 동작하지 않고 무한 대기하는 것을 확인했다(.NET
    /// System.IO.Ports의 알려진 한계 — 취소 토큰이 진행 중인 native 읽기/쓰기를 실제로 중단시키지
    /// 못함). 그래서 이 클래스는 <see cref="SerialPort.ReadTimeout"/>/<see cref="SerialPort.WriteTimeout"/>을
    /// 짧게 잡고 짧은 구간을 반복 시도하면서 매 구간 사이에 CancellationToken과 전체 timeout을
    /// 직접 검사하는 polling 방식으로 Timeout과 취소를 모두 구현한다.</summary>
    private const int PollSliceMs = 50;

    public async Task SendAsync(byte[] data, int timeoutMs, CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureConnectedNoLock();

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var offset = 0;
            while (offset < data.Length)
            {
                ct.ThrowIfCancellationRequested();

                var remainingMs = (deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0)
                    throw new SerialPortTimeoutException(CurrentOptions!.PortName, timeoutMs);

                var sliceMs = (int)Math.Max(1, Math.Min(PollSliceMs, remainingMs));
                _port!.WriteTimeout = sliceMs;

                try
                {
                    var chunk = Math.Min(PollWriteChunkGuard, data.Length - offset);
                    await Task.Run(() => _port.Write(data, offset, chunk), CancellationToken.None).ConfigureAwait(false);
                    offset += chunk;
                }
                catch (TimeoutException)
                {
                    // 이번 구간에는 쓰기가 끝나지 않았다 - 남은 시간이 있으면 계속 시도한다.
                }
                catch (IOException ex)
                {
                    RaisePortLostNoLock(ex);
                    throw new SerialPortCableLostException(CurrentOptions!.PortName, ex);
                }
                catch (InvalidOperationException ex)
                {
                    // 포트가 닫혀 있는데 Read/Write를 시도한 경우 - 이미 다른 경로로 끊어졌다고 보고 정리한다.
                    RaisePortLostNoLock(ex);
                    throw new SerialPortCableLostException(CurrentOptions!.PortName, ex);
                }
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    // Write 자체는 거의 즉시 끝나므로 청크를 나눌 필요가 없지만, 혹시 모를 대형 페이로드에서도
    // 한 번의 폴링 구간 안에서 처리 가능한 크기로 제한해 WriteTimeout이 유효하게 걸리도록 한다.
    private const int PollWriteChunkGuard = 4096;

    public async Task<byte[]> ReceiveAsync(int maxLength, int timeoutMs, CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureConnectedNoLock();

            var buffer = new byte[maxLength];
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var remainingMs = (deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0)
                    throw new SerialPortTimeoutException(CurrentOptions!.PortName, timeoutMs);

                var sliceMs = (int)Math.Max(1, Math.Min(PollSliceMs, remainingMs));
                _port!.ReadTimeout = sliceMs;

                try
                {
                    var read = await Task.Run(() => _port.Read(buffer, 0, maxLength), CancellationToken.None).ConfigureAwait(false);
                    if (read > 0) return buffer[..read];
                }
                catch (TimeoutException)
                {
                    // 이번 구간에는 수신 데이터가 없었다 - 남은 시간이 있으면 계속 대기한다.
                }
                catch (IOException ex)
                {
                    RaisePortLostNoLock(ex);
                    throw new SerialPortCableLostException(CurrentOptions!.PortName, ex);
                }
                catch (InvalidOperationException ex)
                {
                    // 포트가 닫혀 있는데 Read/Write를 시도한 경우 - 이미 다른 경로로 끊어졌다고 보고 정리한다.
                    RaisePortLostNoLock(ex);
                    throw new SerialPortCableLostException(CurrentOptions!.PortName, ex);
                }
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>REQUIREMENTS 5절 — 연결 시점(<see cref="ConnectAsync"/>)이 아니라 그 이후에 쌓일 수
    /// 있는 잔여 바이트(예: 통신 설정 화면의 연결 확인 프로브가 짧은 timeout으로 포기했는데 그 응답이
    /// 조금 늦게 도착해 아무도 읽지 않고 버퍼에 남은 경우)를 비운다. 연결돼 있지 않으면 조용히
    /// 아무 일도 하지 않는다(호출자가 매번 State를 먼저 확인할 필요 없게).</summary>
    public async Task DiscardBuffersAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_port is null || State != SerialConnectionState.Connected) return;
            try
            {
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
            }
            catch (IOException ex)
            {
                RaisePortLostNoLock(ex);
                throw new SerialPortCableLostException(CurrentOptions!.PortName, ex);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private void EnsureConnectedNoLock()
    {
        if (State != SerialConnectionState.Connected || _port is null)
            throw new InvalidOperationException("포트가 연결되어 있지 않습니다.");
    }

    /// <summary>송수신 도중 케이블 분리 등으로 포트를 잃었을 때 호출한다. 이미 _ioLock을 쥔
    /// 상태에서 호출되므로 여기서는 다시 잠그지 않고 내부 정리만 수행한다.</summary>
    private void RaisePortLostNoLock(Exception ex)
    {
        ClosePortInternal();
        State = SerialConnectionState.Failed;
        PortLost?.Invoke(this, ex.Message);
    }

    private void ClosePortInternal()
    {
        if (_port is null) return;
        try
        {
            if (_port.IsOpen) _port.Close();
        }
        catch (IOException)
        {
            // 이미 물리적으로 끊어진 포트를 닫으려 할 때도 예외 없이 자원만 정리한다.
        }
        finally
        {
            _port.Dispose();
            _port = null;
        }
    }

    public void Dispose()
    {
        ClosePortInternal();
        _ioLock.Dispose();
    }
}
