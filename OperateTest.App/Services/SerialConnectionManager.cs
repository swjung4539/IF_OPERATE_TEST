using OperateTest.App.Models;
using OperateTest.App.Services.Serial;

namespace OperateTest.App.Services;

public interface ISerialConnectionManager
{
    IReadOnlyList<string> GetPortNames();
    SerialConnection Get(string channelKey);
    ISerialPortService GetTransport(string channelKey);
    Task ConnectAsync(SerialChannelDefinition definition, string portName, CancellationToken ct = default);
    Task ConnectVerifiedAsync(SerialChannelDefinition definition, string portName,
        Func<CancellationToken, Task> verify, CancellationToken ct = default);
    Task DisconnectAsync(string channelKey, CancellationToken ct = default);
    Task DisconnectManyAsync(IEnumerable<string> channelKeys, CancellationToken ct = default);
    Task DisconnectAllAsync(CancellationToken ct = default);
}

public sealed class SerialConnectionManager : ISerialConnectionManager, IDisposable, IAsyncDisposable
{
    private readonly ISerialPortCatalog _catalog;
    private readonly Dictionary<string, SerialConnection> _connections = new(StringComparer.OrdinalIgnoreCase);

    public SerialConnectionManager(ISerialPortCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<string> GetPortNames() => _catalog.GetPortNames();

    public SerialConnection Get(string channelKey)
    {
        if (!_connections.TryGetValue(channelKey, out var connection))
        {
            connection = new SerialConnection(channelKey);
            _connections.Add(channelKey, connection);
        }
        return connection;
    }

    public ISerialPortService GetTransport(string channelKey) => Get(channelKey).Transport;

    public async Task ConnectAsync(SerialChannelDefinition definition, string portName, CancellationToken ct = default)
    {
        var target = Get(definition.Key);
        if (_connections.Values.Any(item =>
                !ReferenceEquals(item, target) &&
                item.IsConnected &&
                string.Equals(item.PortName, portName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{portName}은(는) 이미 다른 검사 경로에서 사용 중입니다.");
        }

        await target.ConnectAsync(portName, definition.Options, ct: ct).ConfigureAwait(false);
    }

    public async Task ConnectVerifiedAsync(SerialChannelDefinition definition, string portName,
        Func<CancellationToken, Task> verify, CancellationToken ct = default)
    {
        var target = Get(definition.Key);
        if (_connections.Values.Any(item => !ReferenceEquals(item, target) && item.IsConnected &&
                string.Equals(item.PortName, portName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{portName}은(는) 이미 다른 검사 경로에서 사용 중입니다.");
        await target.ConnectAsync(portName, definition.Options, verify, ct).ConfigureAwait(false);
    }

    public Task DisconnectAsync(string channelKey, CancellationToken ct = default) =>
        Get(channelKey).DisconnectAsync(ct);

    public async Task DisconnectManyAsync(IEnumerable<string> channelKeys, CancellationToken ct = default)
    {
        List<Exception>? failures = null;
        foreach (var key in channelKeys)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await Get(key).DisconnectAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(new IOException($"{key} 포트 해제 실패: {ex.Message}", ex));
            }
        }
        if (failures is not null) throw new AggregateException("일부 통신 포트를 해제하지 못했습니다.", failures);
    }

    public Task DisconnectAllAsync(CancellationToken ct = default) =>
        DisconnectManyAsync(_connections.Keys.ToArray(), ct);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await SafetyOperationService.RunWithTimeoutAsync(
                "통신 포트 해제",
                SafetyOperationService.SerialDisconnectTimeout,
                DisconnectAllAsync).ConfigureAwait(false);
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var connection in _connections.Values) connection.Dispose();
        _connections.Clear();
    }
}
