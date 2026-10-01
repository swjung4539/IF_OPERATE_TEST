using OperateTest.App.Services.Gst;
using OperateTest.App.Services.Modbus;
using OperateTest.App.Services.ModbusSlave;

namespace OperateTest.App.Services;

public interface IInspectionProtocolServices
{
    IModbusMasterService GetModbusMaster(string channelKey);
    IModbusSlaveEmulatorService CreateModbusSlaveEmulator(
        string channelKey,
        ModbusSlaveRegisterStore primaryStore,
        IEnumerable<ModbusSlaveRegisterStore> busStores);
    IGstPeerService GetGstPeer(string channelKey);
}

public sealed class InspectionProtocolServices : IInspectionProtocolServices
{
    private readonly ISerialConnectionManager _connections;
    private readonly Dictionary<string, IModbusMasterService> _masters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IGstPeerService> _gstPeers = new(StringComparer.OrdinalIgnoreCase);

    public InspectionProtocolServices(ISerialConnectionManager connections)
    {
        _connections = connections;
    }

    public IModbusMasterService GetModbusMaster(string channelKey)
    {
        if (!_masters.TryGetValue(channelKey, out var master))
        {
            master = new ModbusMasterService(_connections.GetTransport(channelKey));
            _masters.Add(channelKey, master);
        }
        return master;
    }

    public IModbusSlaveEmulatorService CreateModbusSlaveEmulator(
        string channelKey,
        ModbusSlaveRegisterStore primaryStore,
        IEnumerable<ModbusSlaveRegisterStore> busStores) =>
        new ModbusSlaveEmulatorService(_connections.GetTransport(channelKey), primaryStore, busStores);

    public IGstPeerService GetGstPeer(string channelKey)
    {
        if (!_gstPeers.TryGetValue(channelKey, out var peer))
        {
            peer = new GstPeerService(_connections.GetTransport(channelKey));
            _gstPeers.Add(channelKey, peer);
        }
        return peer;
    }
}