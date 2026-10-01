namespace OperateTest.App.Services.Gst;

/// <summary>정상 GST 프레임을 <see cref="GstFaultKind"/>대로 변형하는 순수 함수(I/O 없음).
/// Header/Tail/Crc/Length는 그 필드 하나만 훼손하고 나머지는 정상 값을 유지해, "이 필드 하나만의
/// 결함"을 서로 분리해서 검사할 수 있게 한다.</summary>
public static class GstFrameMutator
{
    public static byte[] Mutate(byte functionCode, ushort address, ushort[] registers, GstFaultKind kind)
    {
        switch (kind)
        {
            case GstFaultKind.Header:
            {
                var frame = GstFrameCodec.Build(functionCode, address, registers);
                frame[0] = 0x00; // 'G'(0x47)를 깨뜨림
                return frame;
            }
            case GstFaultKind.Tail:
            {
                var frame = GstFrameCodec.Build(functionCode, address, registers);
                frame[^1] ^= 0xFF;
                return frame;
            }
            case GstFaultKind.Crc:
            {
                var frame = GstFrameCodec.Build(functionCode, address, registers);
                var crcOffset = 7 + registers.Length * 2;
                frame[crcOffset] ^= 0xFF;
                return frame;
            }
            case GstFaultKind.FunctionCode:
                return GstFrameCodec.Build(0x09, address, registers); // Read(0x01)/Write(0x02) 어느 쪽도 아님
            case GstFaultKind.Address:
                return GstFrameCodec.Build(functionCode, 60000, registers); // Write/Read 유효 범위 밖
            case GstFaultKind.Length:
                return BuildWithBadLength(functionCode, address, registers);
            case GstFaultKind.Endian:
                return GstFrameCodec.Build(functionCode, address, registers, littleEndianData: false);
            case GstFaultKind.PartialFrame:
            {
                var frame = GstFrameCodec.Build(functionCode, address, registers);
                return frame.AsSpan(0, Math.Max(1, frame.Length - 4)).ToArray();
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>Length 필드만 거짓으로 부풀린다. CRC/Tail은 원래(정상) 데이터 기준으로 그대로 두므로,
    /// 파서가 거짓 Length로 계산한 기대 길이와 실제 수신 길이가 어긋나 Length 검증에서 곧바로
    /// 걸러진다(CRC까지 일부러 맞추지 않는 것이 이 Fault의 목적).</summary>
    private static byte[] BuildWithBadLength(byte functionCode, ushort address, ushort[] registers)
    {
        var frame = GstFrameCodec.Build(functionCode, address, registers);
        var trueDataLen = frame[6];
        frame[6] = (byte)Math.Min(trueDataLen + 4, 255);
        return frame;
    }
}
