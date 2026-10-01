using OperateTest.App.Models;
using OperateTest.App.Services.Serial;

namespace OperateTest.App.Services;

public interface ISerialConnectionManager
{
    IReadOnlyList<string> GetPortNames();
    SerialConnection Get(string channelKey);
    ISerialPortService GetTransport(string channelKey);
    Task ConnectAsync(SerialChannelDefinition definition, string portName, CancellationToken ct = default);
    Task DisconnectAsync(string channelKey, CancellationToken ct = default);
    void DisconnectMany(IEnumerable<string> channelKeys);
}

public sealed class SerialConnectionManager : ISerialConnectionManager, IDisposable
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

        await target.ConnectAsync(portName, definition.Options, ct).ConfigureAwait(false);
    }

    public Task DisconnectAsync(string channelKey, CancellationToken ct = default) =>
        Get(channelKey).DisconnectAsync(ct);

    public void DisconnectMany(IEnumerable<string> channelKeys)
    {
        foreach (var key in channelKeys)
        {
            try { Get(key).DisconnectAsync().GetAwaiter().GetResult(); }
            catch { }
        }
    }

    public void Dispose()
    {
        foreach (var connection in _connections.Values) connection.Dispose();
        _connections.Clear();
    }
}
