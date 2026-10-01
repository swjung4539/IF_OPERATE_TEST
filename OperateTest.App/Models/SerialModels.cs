using System.IO.Ports;

namespace OperateTest.App.Models;

public enum SerialConnectionState
{
    Disconnected,
    Connected,
    Error
}

public sealed record SerialConnectionOptions(
    int BaudRate,
    Parity Parity = Parity.None,
    int DataBits = 8,
    StopBits StopBits = StopBits.One,
    int ReadTimeoutMs = 500,
    int WriteTimeoutMs = 500);

public sealed record SerialChannelDefinition(
    string Key,
    string DisplayName,
    string InterfaceName,
    SerialConnectionOptions Options);

public sealed record SerialConnectionSnapshot(
    string ChannelKey,
    SerialConnectionState State,
    string? PortName,
    string Message);
