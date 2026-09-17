namespace FirmwareUpdater;

/// <summary>
/// MCU의 부트로더/앱과 주고받는 USB CDC 프레임 프로토콜.
/// 펌웨어 쪽 Shared/bootloader_shared.h, Bootloader/.../boot_protocol.c,
/// IF_main/.../a_boot_link.c 와 반드시 1:1로 맞아야 하는 상수/알고리즘들이다.
///
/// 프레임 형식(모든 다바이트 필드는 리틀엔디안):
///   [0xAA][0x55][CMD:1][LEN:2][PAYLOAD:LEN][CRC16:2]
/// CRC16은 SOF 2바이트를 제외한 CMD+LEN+PAYLOAD 구간에 대해 계산한다.
/// </summary>
public static class BootProtocol
{
    public const byte Sof0 = 0xAA;
    public const byte Sof1 = 0x55;

    // 부트로더 쪽 BlCmd_t (bootloader_shared.h)와 동일한 값이어야 한다.
    public enum Cmd : byte
    {
        EnterUpdate = 0x00,
        Ping = 0x01,
        EraseSlot = 0x02,
        WriteChunk = 0x03,
        Finalize = 0x04,
        Reboot = 0x05,
        ReadFlash = 0x06,
    }

    // BL_SLOT_A_BASE / BL_SLOT_B_BASE (Shared/bootloader_shared.h와 동일해야 함)
    public const uint SlotABase = 0x0800C000;
    public const uint SlotBBase = 0x08044000;

    // BlSlotId_t와 동일.
    public enum SlotId : byte
    {
        SlotA = 0,
        SlotB = 1,
    }

    /// <summary>
    /// 요청 프레임을 만든다. cmd/payload로부터 [SOF][CMD][LEN][PAYLOAD][CRC16]을 조립한다.
    /// </summary>
    public static byte[] BuildFrame(Cmd cmd, byte[] payload)
    {
        int len = payload.Length;
        var frame = new byte[5 + len + 2];
        frame[0] = Sof0;
        frame[1] = Sof1;
        frame[2] = (byte)cmd;
        frame[3] = (byte)(len & 0xFF);
        frame[4] = (byte)((len >> 8) & 0xFF);
        Array.Copy(payload, 0, frame, 5, len);

        // CRC16은 SOF 2바이트를 뺀 CMD~PAYLOAD 구간(frame[2..(5+len)])에 대해 계산한다.
        ushort crc = Crc16(frame, 2, 3 + len);
        frame[5 + len] = (byte)(crc & 0xFF);
        frame[5 + len + 1] = (byte)((crc >> 8) & 0xFF);
        return frame;
    }

    /// <summary>
    /// 응답 프레임 하나를 담는 단순 컨테이너. Cmd는 요청과 동일하게 에코되어 온다.
    /// Payload[0]은 관례상 상태 바이트(0=OK, 1=FAIL)이고, 그 뒤에 커맨드별 데이터가 붙는다.
    /// </summary>
    public sealed record Frame(Cmd Cmd, byte[] Payload)
    {
        public bool IsOk => Payload.Length > 0 && Payload[0] == 0;
    }

    /// <summary>
    /// MCU 쪽 boot_protocol.c / a_boot_link.c의 수신 상태머신과 동일한 구조로,
    /// SerialPort에서 바이트를 하나씩 읽어 완전한 프레임 하나를 만들어낸다.
    /// 타임아웃(밀리초) 안에 완전한 프레임을 못 받으면 null을 반환한다.
    /// </summary>
    public static Frame? ReadFrame(Stream stream, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        int state = 0; // 0:SOF0 1:SOF1 2:CMD 3:LEN0 4:LEN1 5:PAYLOAD 6:CRC0 7:CRC1
        byte cmd = 0;
        int len = 0;
        int payloadIndex = 0;
        byte[] payload = Array.Empty<byte>();
        ushort crcReceived = 0;

        while (DateTime.UtcNow < deadline)
        {
            int b = ReadByteWithTimeout(stream, deadline);
            if (b < 0)
            {
                // 타임아웃 - 지금까지 받은 게 있어도 미완성 프레임은 버린다.
                return null;
            }
            byte value = (byte)b;

            switch (state)
            {
                case 0:
                    state = value == Sof0 ? 1 : 0;
                    break;
                case 1:
                    state = value == Sof1 ? 2 : 0;
                    break;
                case 2:
                    cmd = value;
                    state = 3;
                    break;
                case 3:
                    len = value;
                    state = 4;
                    break;
                case 4:
                    len |= value << 8;
                    if (len > 4096)
                    {
                        // 있을 수 없는 길이 - 잡음으로 보고 재동기화.
                        state = 0;
                    }
                    else
                    {
                        payload = new byte[len];
                        payloadIndex = 0;
                        state = len == 0 ? 6 : 5;
                    }
                    break;
                case 5:
                    payload[payloadIndex++] = value;
                    if (payloadIndex >= len)
                    {
                        state = 6;
                    }
                    break;
                case 6:
                    crcReceived = value;
                    state = 7;
                    break;
                case 7:
                    crcReceived |= (ushort)(value << 8);

                    // CRC 검증: CMD(1)+LEN(2)+PAYLOAD(len) 버퍼를 다시 조립해서 계산.
                    var headerAndPayload = new byte[3 + len];
                    headerAndPayload[0] = cmd;
                    headerAndPayload[1] = (byte)(len & 0xFF);
                    headerAndPayload[2] = (byte)((len >> 8) & 0xFF);
                    Array.Copy(payload, 0, headerAndPayload, 3, len);
                    ushort computed = Crc16(headerAndPayload, 0, headerAndPayload.Length);

                    if (computed != crcReceived)
                    {
                        // CRC 불일치 - 이 프레임은 버리고 처음부터 다시 찾는다.
                        state = 0;
                        break;
                    }

                    return new Frame((Cmd)cmd, payload);
            }
        }

        return null;
    }

    private static int ReadByteWithTimeout(Stream stream, DateTime deadline)
    {
        // SerialPort의 ReadTimeout을 그대로 쓰지 않고, 프레임 전체에 대한
        // deadline을 넘겨서 "프레임 하나를 기다리는 총 시간"을 직접 통제한다.
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return stream.ReadByte();
            }
            catch (TimeoutException)
            {
                // 짧은 개별 read 타임아웃은 무시하고 전체 deadline까지 계속 시도한다.
            }
        }
        return -1;
    }

    /// <summary>
    /// Modbus와 동일한 CRC16(poly 0xA001, 초기값 0xFFFF). 펌웨어 쪽
    /// BootShared_Crc16()과 반드시 동일한 알고리즘이어야 한다.
    /// </summary>
    public static ushort Crc16(byte[] data, int offset, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[offset + i];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                }
                else
                {
                    crc = (ushort)(crc >> 1);
                }
            }
        }
        return crc;
    }

    /// <summary>
    /// 표준 CRC32(poly 0xEDB88320, 초기값/최종 XOR 모두 0xFFFFFFFF). 펌웨어 쪽
    /// BootShared_Crc32()와 반드시 동일한 알고리즘이어야 한다(FINALIZE 검증에 사용).
    /// </summary>
    public static uint Crc32(byte[] data, int offset, int length)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[offset + i];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ 0xEDB88320;
                }
                else
                {
                    crc = crc >> 1;
                }
            }
        }
        return crc ^ 0xFFFFFFFF;
    }
}
