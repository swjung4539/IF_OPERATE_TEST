using OperateTest.App.Services.Modbus;

namespace OperateTest.App.Services.Gst;

/// <summary>GST 프레임 생성/파싱만 담당하는 순수 함수 모음(I/O 없음). 프레임 구조와 CRC는
/// COMMUNICATION_SPEC.md 11절과 `a_gst_protocol.c`(`BuildWriteFrame`/`ValidateFrame`/`ApplyReadFrame`)
/// 그대로다: `[Header 3][FC 1][Address Hi/Lo 2][Length 1][Data N][CRC16 Lo/Hi 2][Tail 2]`.
/// CRC16은 Modbus RTU와 같은 알고리즘을 재사용한다고 원본 주석에 명시되어 있어(11절)
/// <see cref="ModbusRtuCodec.ComputeCrc16"/>를 그대로 쓴다(새로 구현하지 않음).</summary>
public static class GstFrameCodec
{
    public const byte Header0 = 0x47, Header1 = 0x53, Header2 = 0x54; // "GST"
    public const byte Tail0 = 0x53, Tail1 = 0x49; // "SI"
    public const int RegisterCount = 41;
    public const int MaxDataLength = RegisterCount * 2;
    public const int MinFrameLength = 11; // Header3+FC1+Addr2+Length1+CRC2+Tail2, Data 0개
    public const int MaxFrameLength = MinFrameLength + MaxDataLength; // 93 (GST_PROTOCOL_MAX_FRAME_LENGTH)

    /// <summary>정상 GST 프레임을 만든다. Address 필드는 빅엔디안(Hi,Lo), Data(레지스터) payload는
    /// 레지스터당 리틀엔디안(Lo,Hi)이다 — 다른 채널(Modbus RTU 빅엔디안 레지스터)과 정반대이므로
    /// <paramref name="littleEndianData"/>를 false로 주면 Endian Fault(데이터를 일부러 빅엔디안으로
    /// 뒤집어 보냄)를 만들 수 있다.</summary>
    public static byte[] Build(byte functionCode, ushort address, ushort[] registers, bool littleEndianData = true)
    {
        if (registers.Length == 0 || registers.Length > RegisterCount)
            throw new ArgumentOutOfRangeException(nameof(registers), $"GST 레지스터 수는 1~{RegisterCount}개여야 합니다.");

        var dataLen = registers.Length * 2;
        var frame = new byte[MinFrameLength + dataLen];
        frame[0] = Header0;
        frame[1] = Header1;
        frame[2] = Header2;
        frame[3] = functionCode;
        frame[4] = (byte)(address >> 8);
        frame[5] = (byte)(address & 0xFF);
        frame[6] = (byte)dataLen;

        for (var i = 0; i < registers.Length; i++)
        {
            var v = registers[i];
            if (littleEndianData)
            {
                frame[7 + i * 2] = (byte)(v & 0xFF);
                frame[7 + i * 2 + 1] = (byte)(v >> 8);
            }
            else
            {
                frame[7 + i * 2] = (byte)(v >> 8);
                frame[7 + i * 2 + 1] = (byte)(v & 0xFF);
            }
        }

        var crcOffset = 7 + dataLen;
        var crc = ModbusRtuCodec.ComputeCrc16(frame.AsSpan(0, crcOffset));
        frame[crcOffset] = (byte)(crc & 0xFF);
        frame[crcOffset + 1] = (byte)(crc >> 8);
        frame[crcOffset + 2] = Tail0;
        frame[crcOffset + 3] = Tail1;
        return frame;
    }

    /// <summary>완전한 프레임 하나를 파싱/검증한다. 검증 순서는 `a_gst_protocol.c ValidateFrame`과
    /// 동일하다: Header+FC → Length(짝수, 전체 길이 일치) → Tail → CRC. 실제 컨트롤러는 이 중
    /// 무엇이 실패했는지 구분하지 않고 그냥 "무효 프레임, 버림"으로만 처리한다는 점에 유의한다 —
    /// 이 파서는 에뮬레이터 쪽 로그/Fault 분류를 세분화하기 위한 것이지, 실제 컨트롤러가 원인을
    /// 알려준다는 뜻이 아니다.</summary>
    public static GstFrame Parse(byte[] frame)
    {
        if (frame.Length < MinFrameLength)
            throw new GstIncompleteFrameException($"길이 {frame.Length}바이트로 최소 프레임 길이({MinFrameLength})에도 못 미침");

        if (frame[0] != Header0 || frame[1] != Header1 || frame[2] != Header2)
            throw new GstHeaderMismatchException($"실제 0x{frame[0]:X2} 0x{frame[1]:X2} 0x{frame[2]:X2}(기대 'GST')");

        var fc = frame[3];
        if (fc != GstFunctionCode.Read && fc != GstFunctionCode.Write)
            throw new GstFunctionCodeInvalidException(fc);

        var address = (ushort)((frame[4] << 8) | frame[5]);
        var dataLen = frame[6];
        var expectedLen = MinFrameLength + dataLen;
        if (dataLen % 2 != 0 || frame.Length != expectedLen)
            throw new GstLengthMismatchException($"length 필드={dataLen}, 실제 프레임 길이={frame.Length}(기대 {expectedLen})");

        var crcOffset = 7 + dataLen;
        if (frame[crcOffset + 2] != Tail0 || frame[crcOffset + 3] != Tail1)
            throw new GstTailMismatchException($"실제 0x{frame[crcOffset + 2]:X2} 0x{frame[crcOffset + 3]:X2}(기대 'SI')");

        var expectedCrc = ModbusRtuCodec.ComputeCrc16(frame.AsSpan(0, crcOffset));
        var actualCrc = (ushort)(frame[crcOffset] | (frame[crcOffset + 1] << 8));
        if (expectedCrc != actualCrc)
            throw new GstCrcMismatchException(expectedCrc, actualCrc);

        var quantity = dataLen / 2;
        var registers = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
        {
            // GST 디바이스는 레지스터당 리틀엔디안(Lo, Hi) — a_gst_protocol.c:249와 동일.
            registers[i] = (ushort)(frame[7 + i * 2] | (frame[7 + i * 2 + 1] << 8));
        }

        return new GstFrame(fc, address, registers, frame);
    }

    /// <summary>수신 누적 중 프레임이 완성됐는지 판단하기 위한 예상 전체 길이. Header+FC+Address+
    /// Length 필드(7바이트)까지 받아야 계산 가능하고, 그 전에는 null(더 받아야 함).</summary>
    public static int? TryComputeExpectedLength(IReadOnlyList<byte> received)
    {
        if (received.Count < 7) return null;
        return MinFrameLength + received[6];
    }
}
