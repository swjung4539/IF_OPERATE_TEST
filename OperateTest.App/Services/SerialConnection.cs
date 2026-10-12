using OperateTest.App.Models;
using OperateTest.App.Services.Serial;
using ProtocolConnectionState = OperateTest.App.Services.Serial.SerialConnectionState;
using UiConnectionState = OperateTest.App.Models.SerialConnectionState;

namespace OperateTest.App.Services;

public sealed class SerialConnection : IDisposable
{
    private readonly object _sync = new();
    private readonly ISerialPortService _transport;

    public SerialConnection(string channelKey, ISerialPortService? transport = null)
    {
        ChannelKey = channelKey;
        _transport = transport ?? new SerialPortService();
        _transport.PortLost += TransportOnPortLost;
    }

    public string ChannelKey { get; }
    public UiConnectionState State { get; private set; } = UiConnectionState.Disconnected;
    public string? PortName => _transport.CurrentOptions?.PortName;
    public string Message { get; private set; } = "연결 안 됨";
    public bool IsConnected => State == UiConnectionState.Connected &&
                               _transport.State == ProtocolConnectionState.Connected;
    public ISerialPortService Transport => _transport;

    public event EventHandler<SerialConnectionSnapshot>? StateChanged;

    public async Task ConnectAsync(string portName, SerialConnectionOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("COM 포트를 선택하세요.", nameof(portName));

        lock (_sync)
        {
            State = UiConnectionState.Disconnected;
            Message = "연결 중...";
        }
        RaiseStateChanged();

        try
        {
            if (_transport.State == ProtocolConnectionState.Connected)
                await _transport.DisconnectAsync(ct).ConfigureAwait(false);

            await _transport.ConnectAsync(new SerialPortOptions(
                portName,
                options.BaudRate,
                options.DataBits,
                options.Parity,
                options.StopBits), ct).ConfigureAwait(false);

            lock (_sync)
            {
                State = UiConnectionState.Connected;
                Message = "연결됨";
            }
        }
        catch (Exception ex)
        {
            Exception? cleanupError = null;
            try { await _transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception disconnectException) { cleanupError = disconnectException; }

            lock (_sync)
            {
                State = UiConnectionState.Error;
                Message = cleanupError is null ? FriendlyMessage(ex) : "연결 실패 · 포트 해제도 확인 필요";
            }
            RaiseStateChanged();
            if (cleanupError is not null)
                throw new AggregateException("포트 연결 실패 후 통신 포트를 정상 해제하지 못했습니다.", ex, cleanupError);
            throw;
        }

        RaiseStateChanged();
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        try
        {
            await _transport.DisconnectAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                State = UiConnectionState.Disconnected;
                Message = "연결 안 됨";
            }
            RaiseStateChanged();
        }
    }

    private void TransportOnPortLost(object? sender, string reason)
    {
        lock (_sync)
        {
            State = UiConnectionState.Error;
            Message = "연결 끊김";
        }
        RaiseStateChanged();
    }

    private static string FriendlyMessage(Exception ex) => ex switch
    {
        SerialPortBusyException => "다른 프로그램이 사용 중입니다",
        SerialPortNotFoundException => "포트를 찾을 수 없습니다",
        SerialPortCableLostException => "연결이 끊어졌습니다",
        UnauthorizedAccessException => "다른 프로그램이 사용 중입니다",
        IOException => "포트를 열 수 없습니다",
        ArgumentException => "포트 설정을 확인하세요",
        OperationCanceledException => "연결이 취소되었습니다",
        _ => "연결 실패"
    };

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(this, new SerialConnectionSnapshot(ChannelKey, State, PortName, Message));

    public void Dispose()
    {
        _transport.PortLost -= TransportOnPortLost;
        _transport.Dispose();
    }
}
