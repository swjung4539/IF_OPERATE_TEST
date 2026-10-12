using System.IO.Ports;

namespace OperateTest.App.Models;

public static class ChannelDefinitions
{
    public static readonly SerialChannelDefinition Uart4 = new(
        "comm-uart4", "UART4 메인 통신", "RS-485 · 115200 · 8-N-1",
        new SerialConnectionOptions(115200));

    public static readonly SerialChannelDefinition DioSlave = new(
        "comm-lpuart1", "DIO Slave 통신", "MAIN 내부 경로 · 상태 맵 판정",
        new SerialConnectionOptions(38400));

    public static readonly SerialChannelDefinition Heating = new(
        "comm-usart2", "Heating Jacket 통신", "RS-485 · 19200 · 8-N-1",
        new SerialConnectionOptions(19200));

    public static readonly SerialChannelDefinition Gas = new(
        "comm-usart3", "Gas Detector 통신", "RS-485 · 9600 · 8-E-1",
        new SerialConnectionOptions(9600, Parity.Even));

    public static readonly SerialChannelDefinition Backup = new(
        "comm-usart1", "Backup GST 통신", "RS-422 · 115200 · 8-N-1",
        new SerialConnectionOptions(115200));

    public static IReadOnlyList<SerialChannelDefinition> CommChannels { get; } =
        [Uart4, Heating, Gas, Backup];

    public static IReadOnlyList<SerialChannelDefinition> CommPortChannels { get; } =
        [Uart4, Heating, Gas, Backup];
}
