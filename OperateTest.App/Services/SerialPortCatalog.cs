using System.IO.Ports;

namespace OperateTest.App.Services;

public interface ISerialPortCatalog
{
    IReadOnlyList<string> GetPortNames();
}

public sealed class SerialPortCatalog : ISerialPortCatalog
{
    public IReadOnlyList<string> GetPortNames() => SerialPort.GetPortNames()
        .OrderBy(PortSortKey)
        .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static int PortSortKey(string name) =>
        int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out var number)
            ? number
            : int.MaxValue;
}
