using System.IO.Ports;

namespace TestMode.App;

internal enum FirmwareUpdateResult { Latest, Success }
internal enum FirmwareCheckResult { Latest, Different }

internal sealed class MainFirmwareUpdateService
{
    private const int ChunkSize = 48;
    internal event Action<int>? ProgressChanged;
    internal event Action<string>? StatusChanged;

    internal async Task<FirmwareUpdateResult> RunAsync(string hexPath, CancellationToken cancellationToken)
    {
        var image = await Task.Run(() => IntelHexImage.Load(hexPath), cancellationToken);
        image.EnsureOnlyMainSlots();
        var slotImages = new Dictionary<BootProtocol.Slot, byte[]>
        {
            [BootProtocol.Slot.A] = image.ToSlotImage(BootProtocol.Slot.A),
            [BootProtocol.Slot.B] = image.ToSlotImage(BootProtocol.Slot.B)
        };
        if (slotImages.Values.All(bytes => bytes.Length == 0)) throw new InvalidDataException("MAIN 슬롯 데이터가 없는 HEX 파일입니다.");

        using var port = await ConnectToBootloaderAsync(cancellationToken);
        var ping = await SendAsync(port, BootProtocol.Command.Ping, Array.Empty<byte>(), 1200, cancellationToken);
        if (ping is null || !ping.IsOk || ping.Payload.Length < 14) throw new InvalidOperationException("부트로더 PING 응답이 올바르지 않습니다.");
        var active = (BootProtocol.Slot)ping.Payload[4];
        var activeImage = slotImages[active];
        if (activeImage.Length > 0 && await MatchesFlashAsync(port, active, activeImage, cancellationToken))
        {
            StatusChanged?.Invoke("선택한 펌웨어와 동일합니다.");
            ProgressChanged?.Invoke(100);
            _ = await SendAsync(port, BootProtocol.Command.Reboot, Array.Empty<byte>(), 500, cancellationToken);
            return FirmwareUpdateResult.Latest;
        }

        var target = active == BootProtocol.Slot.A ? BootProtocol.Slot.B : BootProtocol.Slot.A;
        var targetImage = slotImages[target];
        if (targetImage.Length == 0) throw new InvalidDataException("현재 활성 슬롯 또는 반대 슬롯에 사용할 MAIN 이미지가 없습니다.");
        if (targetImage.Length > BootProtocol.SlotSize) throw new InvalidDataException("MAIN 슬롯 크기를 초과하는 HEX 파일입니다.");

        StatusChanged?.Invoke($"{(target == BootProtocol.Slot.A ? "Slot A" : "Slot B")} 기록 준비");
        var erase = new byte[5];
        erase[0] = (byte)target;
        BitConverter.GetBytes((uint)targetImage.Length).CopyTo(erase, 1);
        await SendOkAsync(port, BootProtocol.Command.EraseSlot, erase, 6000, cancellationToken);

        for (var offset = 0; offset < targetImage.Length; offset += ChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(ChunkSize, targetImage.Length - offset);
            var payload = new byte[4 + length];
            BitConverter.GetBytes((uint)offset).CopyTo(payload, 0);
            Array.Copy(targetImage, offset, payload, 4, length);
            await SendOkAsync(port, BootProtocol.Command.WriteChunk, payload, 1500, cancellationToken);
            ProgressChanged?.Invoke((int)((offset + length) * 90L / targetImage.Length));
        }

        StatusChanged?.Invoke("기록 내용 검증 중");
        var finalize = new byte[11];
        BitConverter.GetBytes((uint)targetImage.Length).CopyTo(finalize, 0);
        BitConverter.GetBytes(BootProtocol.Crc32(targetImage)).CopyTo(finalize, 4);
        await SendOkAsync(port, BootProtocol.Command.Finalize, finalize, 3000, cancellationToken);
        ProgressChanged?.Invoke(96);

        StatusChanged?.Invoke("보드 재부팅 중");
        _ = await SendAsync(port, BootProtocol.Command.Reboot, Array.Empty<byte>(), 500, cancellationToken);
        ProgressChanged?.Invoke(100);
        return FirmwareUpdateResult.Success;
    }

    internal async Task<FirmwareCheckResult> VerifyAsync(string hexPath, CancellationToken cancellationToken)
    {
        var image = await Task.Run(() => IntelHexImage.Load(hexPath), cancellationToken);
        image.EnsureOnlyMainSlots();
        var slotImages = new Dictionary<BootProtocol.Slot, byte[]>
        {
            [BootProtocol.Slot.A] = image.ToSlotImage(BootProtocol.Slot.A),
            [BootProtocol.Slot.B] = image.ToSlotImage(BootProtocol.Slot.B)
        };
        using var port = await ConnectToBootloaderAsync(cancellationToken);
        var ping = await SendAsync(port, BootProtocol.Command.Ping, Array.Empty<byte>(), 1200, cancellationToken);
        if (ping is null || !ping.IsOk || ping.Payload.Length < 14)
            throw new InvalidOperationException("부트로더 PING 응답이 올바르지 않습니다.");

        var active = (BootProtocol.Slot)ping.Payload[4];
        var expected = slotImages[active];
        if (expected.Length == 0)
            throw new InvalidDataException("선택한 HEX에 현재 MAIN 슬롯 이미지가 없습니다.");

        StatusChanged?.Invoke("현재 펌웨어 내용 확인 중");
        var same = await MatchesFlashAsync(port, active, expected, cancellationToken);
        StatusChanged?.Invoke("운용 펌웨어로 복귀 중");
        _ = await SendAsync(port, BootProtocol.Command.Reboot, Array.Empty<byte>(), 500, cancellationToken);
        ProgressChanged?.Invoke(100);
        return same ? FirmwareCheckResult.Latest : FirmwareCheckResult.Different;
    }

    private async Task<bool> MatchesFlashAsync(SerialPort port, BootProtocol.Slot slot, byte[] expected, CancellationToken ct)
    {
        var baseAddress = slot == BootProtocol.Slot.A ? BootProtocol.SlotABase : BootProtocol.SlotBBase;
        for (var offset = 0; offset < expected.Length; offset += ChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var length = Math.Min(ChunkSize, expected.Length - offset);
            var payload = new byte[6];
            BitConverter.GetBytes(baseAddress + (uint)offset).CopyTo(payload, 0);
            BitConverter.GetBytes((ushort)length).CopyTo(payload, 4);
            var response = await SendAsync(port, BootProtocol.Command.ReadFlash, payload, 1200, ct);
            if (response is null || !response.IsOk || response.Payload.Length < length + 1)
                throw new IOException("MAIN 펌웨어 읽기 응답이 올바르지 않습니다.");
            if (!response.Payload.AsSpan(1, length).SequenceEqual(expected.AsSpan(offset, length))) return false;
        }
        return true;
    }

    private static async Task<SerialPort> ConnectToBootloaderAsync(CancellationToken ct)
    {
        foreach (var name in SerialPort.GetPortNames().OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            SerialPort? port = null;
            try
            {
                port = Open(name);
                var ping = await SendAsync(port, BootProtocol.Command.Ping, Array.Empty<byte>(), 600, ct);
                if (ping?.IsOk == true) return port;
                port.Dispose();
            }
            catch (Exception) { port?.Dispose(); }
        }

        foreach (var name in SerialPort.GetPortNames().OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var port = Open(name);
                var enter = BootProtocol.BuildFrame(BootProtocol.Command.EnterUpdate, Array.Empty<byte>());
                port.Write(enter, 0, enter.Length);
                await Task.Delay(1500, ct);
            }
            catch (Exception) { }
        }

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var name in SerialPort.GetPortNames().OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var port = Open(name);
                    var ping = await SendAsync(port, BootProtocol.Command.Ping, Array.Empty<byte>(), 600, ct);
                    if (ping?.IsOk == true) return port;
                    port.Dispose();
                }
                catch (Exception) { }
            }
            await Task.Delay(300, ct);
        }
        throw new InvalidOperationException("MAIN 부트로더를 찾지 못했습니다. USB 연결을 확인하세요.");
    }

    private static async Task SendOkAsync(SerialPort port, BootProtocol.Command command, byte[] payload, int timeout, CancellationToken ct)
    {
        var response = await SendAsync(port, command, payload, timeout, ct);
        if (response is null || !response.IsOk) throw new InvalidOperationException($"부트로더 {command} 명령이 실패했습니다.");
    }

    private static async Task<BootProtocol.Frame?> SendAsync(SerialPort port, BootProtocol.Command command, byte[] payload, int timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var frame = BootProtocol.BuildFrame(command, payload);
        port.Write(frame, 0, frame.Length);
        var response = await Task.Run(() => BootProtocol.ReadFrame(port.BaseStream, timeout), ct);
        return response;
    }

    private static SerialPort Open(string name)
    {
        var port = new SerialPort(name, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 100,
            WriteTimeout = 2000
        };
        port.Open();
        return port;
    }
}
