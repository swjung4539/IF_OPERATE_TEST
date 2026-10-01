namespace OperateTest.App.Services.Gst;

/// <summary>GST 프레임 파싱/검증 단계에서만 발생하는 예외의 공통 기반. Serial 계층 예외
/// (<see cref="OperateTest.App.Services.Serial.SerialLayerException"/>)와 구분된다.</summary>
public abstract class GstProtocolException : Exception
{
    protected GstProtocolException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class GstHeaderMismatchException : GstProtocolException
{
    public GstHeaderMismatchException(string detail) : base("Header 불일치: " + detail)
    {
    }
}

public sealed class GstTailMismatchException : GstProtocolException
{
    public GstTailMismatchException(string detail) : base("Tail 불일치: " + detail)
    {
    }
}

public sealed class GstCrcMismatchException : GstProtocolException
{
    public ushort ExpectedCrc { get; }
    public ushort ActualCrc { get; }

    public GstCrcMismatchException(ushort expected, ushort actual)
        : base($"CRC 불일치: 계산값 0x{expected:X4}, 프레임값 0x{actual:X4}")
    {
        ExpectedCrc = expected;
        ActualCrc = actual;
    }
}

public sealed class GstFunctionCodeInvalidException : GstProtocolException
{
    public byte ActualFunctionCode { get; }

    public GstFunctionCodeInvalidException(byte actual)
        : base($"Function Code 0x{actual:X2}는 GST Read(0x01)/Write(0x02) 어느 쪽도 아님")
    {
        ActualFunctionCode = actual;
    }
}

public sealed class GstLengthMismatchException : GstProtocolException
{
    public GstLengthMismatchException(string detail) : base("Length 불일치: " + detail)
    {
    }
}

/// <summary>프레임이 아직 다 도착하지 않았거나(최소 길이 미달), timeout까지도 완성되지 않은 채로
/// 남은 경우(부분 프레임).</summary>
public sealed class GstIncompleteFrameException : GstProtocolException
{
    public GstIncompleteFrameException(string detail) : base("불완전한 프레임(부분 프레임): " + detail)
    {
    }
}
