using OperateTest.App.Forms;
using OperateTest.App.Services;
using OperateTest.App.Services.StLink;

namespace OperateTest.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var connections = new SerialConnectionManager(new SerialPortCatalog());
        var protocols = new InspectionProtocolServices(connections);
        var stLinkMemory = new StLinkMemoryReader();
        var dioIdReader = new StLinkDioIdSwitchReader(stLinkMemory);
        var rotaryReader = new StLinkCommRotaryReader(stLinkMemory);
        Application.Run(new HomeForm(connections, protocols, dioIdReader, rotaryReader));
    }
}
