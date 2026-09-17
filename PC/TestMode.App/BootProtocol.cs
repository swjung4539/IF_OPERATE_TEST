using System.Buffers.Binary;

namespace TestMode.App;

internal static class BootProtocol
{
    internal const byte Sof0 = 0xAA;
    internal const byte Sof1 = 0x55;
    internal const uint SlotABase = 0x0800C000;
    internal const uint SlotBBase = 0x08044000;
    internal const uint SlotSize = 224U * 1024U;

    internal enum Command : byte { EnterUpdate = 0x00, Ping = 0x01, EraseSlot = 0x02, WriteChunk = 0x03, Finalize = 0x04, Reboot = 0x05, ReadFlash = 0x06 }
    internal enum Slot : byte { A = 0, B = 1 }
    internal sealed record Frame(Command Command, byte[] Payload) { internal bool IsOk => Payload.Length > 0 && Payload[0] == 0; }

    internal static byte[] BuildFrame(Command command, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[7 + payload.Length];
        frame[0] = Sof0; frame[1] = Sof1; frame[2] = (byte)command;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), (ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(5));
        var crc = Crc16(frame.AsSpan(2, 3 + payload.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5 + payload.Length), crc);
        return frame;
    }

    internal static Frame? ReadFrame(Stream stream, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var bytes = new List<byte>();
        while (DateTime.UtcNow < deadline)
        {
            int value;
            try { value = stream.ReadByte(); }
            catch (TimeoutException) { continue; }
            if (value < 0) return null;
            bytes.Add((byte)value);
            if (bytes.Count < 5) continue;
            if (bytes[0] != Sof0 || bytes[1] != Sof1) { bytes.RemoveAt(0); continue; }
            var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.ToArray().AsSpan(3, 2));
            if (length > 4096) { bytes.Clear(); continue; }
            var total = 7 + length;
            if (bytes.Count < total) continue;
            var frame = bytes.ToArray();
            var received = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(total - 2));
            if (Crc16(frame.AsSpan(2, total - 4)) != received) { bytes.RemoveAt(0); continue; }
            return new Frame((Command)frame[2], frame.AsSpan(5, length).ToArray());
        }
        return null;
    }

    internal static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (ushort)(((crc & 1) != 0) ? ((crc >> 1) ^ 0xA001) : (crc >> 1));
        }
        return crc;
    }

    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
