namespace TestMode.App;

internal sealed class IntelHexImage
{
    private readonly SortedDictionary<uint, byte> data;
    internal IReadOnlyDictionary<uint, byte> Data => data;

    private IntelHexImage(SortedDictionary<uint, byte> data) => this.data = data;

    internal static IntelHexImage Load(string path)
    {
        var bytes = new SortedDictionary<uint, byte>();
        uint upper = 0;
        var lineNumber = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] != ':' || line.Length < 11 || (line.Length - 1) % 2 != 0)
                throw new InvalidDataException($"Intel HEX 형식이 아닙니다. 줄 {lineNumber}");
            var record = Convert.FromHexString(line[1..]);
            var length = record[0];
            if (record.Length != length + 5 || record.Sum(value => value) % 256 != 0)
                throw new InvalidDataException($"Intel HEX 체크섬이 잘못되었습니다. 줄 {lineNumber}");
            var address = (uint)((record[1] << 8) | record[2]);
            var type = record[3];
            if (type == 0x00)
            {
                for (var index = 0; index < length; index++)
                {
                    var absolute = upper + address + (uint)index;
                    if (bytes.TryGetValue(absolute, out var previous) && previous != record[4 + index])
                        throw new InvalidDataException($"Intel HEX 주소가 중복되고 데이터가 다릅니다. 주소 0x{absolute:X8}");
                    bytes[absolute] = record[4 + index];
                }
            }
            else if (type == 0x01) break;
            else if (type == 0x02 && length == 2) upper = (uint)(((record[4] << 8) | record[5]) << 4);
            else if (type == 0x04 && length == 2) upper = (uint)((record[4] << 24) | (record[5] << 16));
            else if (type != 0x03 && type != 0x05)
                throw new InvalidDataException($"지원하지 않는 Intel HEX 레코드입니다. 유형 0x{type:X2}");
        }
        if (bytes.Count == 0) throw new InvalidDataException("데이터가 없는 Intel HEX 파일입니다.");
        return new IntelHexImage(bytes);
    }

    internal byte[] ToSlotImage(BootProtocol.Slot slot)
    {
        var baseAddress = slot == BootProtocol.Slot.A ? BootProtocol.SlotABase : BootProtocol.SlotBBase;
        var endAddress = baseAddress + BootProtocol.SlotSize;
        var addresses = data.Keys.Where(address => address >= baseAddress && address < endAddress).ToArray();
        if (addresses.Length == 0) return Array.Empty<byte>();
        var length = checked((int)(addresses[^1] - baseAddress + 1));
        var image = Enumerable.Repeat((byte)0xFF, length).ToArray();
        foreach (var address in addresses) image[address - baseAddress] = data[address];
        return image;
    }

    internal void EnsureOnlyMainSlots()
    {
        foreach (var address in data.Keys)
        {
            var inA = address >= BootProtocol.SlotABase && address < BootProtocol.SlotABase + BootProtocol.SlotSize;
            var inB = address >= BootProtocol.SlotBBase && address < BootProtocol.SlotBBase + BootProtocol.SlotSize;
            if (!inA && !inB) throw new InvalidDataException($"MAIN 앱 슬롯 밖의 주소가 포함되어 있습니다: 0x{address:X8}");
        }
    }

    internal byte[] ToFixedImage(uint baseAddress, uint size)
    {
        var endAddress = checked(baseAddress + size);
        foreach (var address in data.Keys)
            if (address < baseAddress || address >= endAddress)
                throw new InvalidDataException($"DIO Flash 범위 밖의 주소가 포함되어 있습니다: 0x{address:X8}");

        var image = Enumerable.Repeat((byte)0xFF, checked((int)size)).ToArray();
        foreach (var item in data)
            image[item.Key - baseAddress] = item.Value;
        return image;
    }

    internal string ReadAscii(uint address, int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
            bytes[index] = data.GetValueOrDefault(address + (uint)index, (byte)0xFF);
        var terminator = Array.FindIndex(bytes, value => value is 0 or 0xFF);
        if (terminator < 0) terminator = bytes.Length;
        return System.Text.Encoding.ASCII.GetString(bytes, 0, terminator);
    }
}
