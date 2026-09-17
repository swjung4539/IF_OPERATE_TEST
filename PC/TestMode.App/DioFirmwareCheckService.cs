namespace TestMode.App;

internal sealed class DioFirmwareCheckService
{
    internal const uint FlashBase = 0x08000000U;
    internal const uint FlashSize = 128U * 1024U;
    internal const uint InfoAddress = 0x0801FFE0U;
    private readonly UsbTestClient usb;

    internal DioFirmwareCheckService(UsbTestClient usb) => this.usb = usb;

    internal async Task<FirmwareCheckResult> VerifyAsync(string hexPath, CancellationToken cancellationToken)
    {
        var image = await Task.Run(() => IntelHexImage.Load(hexPath), cancellationToken);
        var normalized = image.ToFixedImage(FlashBase, FlashSize);
        var magic = image.ReadAscii(InfoAddress, 8);
        var version = image.ReadAscii(InfoAddress + 8U, 16);
        if (magic != "DIOFW01" || string.IsNullOrWhiteSpace(version))
            throw new InvalidDataException("선택한 HEX에 DIO 펌웨어 정보가 없습니다.");

        var connected = await usb.EnsureConnectedAsync(Board.Dio);
        if (!connected)
            throw new InvalidOperationException("DIO USB 장치를 찾을 수 없습니다.");

        var board = await usb.ReadDioFirmwareInfoAsync();
        if (board is null)
            throw new InvalidOperationException("DIO 펌웨어 정보를 읽을 수 없습니다.");

        var crc = BootProtocol.Crc32(normalized);
        return board.Size == FlashSize
            && board.Crc == crc
            && string.Equals(board.Version, version, StringComparison.Ordinal)
            ? FirmwareCheckResult.Latest
            : FirmwareCheckResult.Different;
    }
}
