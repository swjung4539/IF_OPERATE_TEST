namespace OperateTest.App.Services.ModbusSlave;

/// <summary>정상 응답 프레임 하나를 받아 <see cref="ModbusSlaveFaultKind"/>에 따라 결함 있는 형태로
/// 바꾸는 순수 함수 모음(UART4/GST의 FrameMutator와 같은 원칙 — I/O를 하지 않는다).
/// <see cref="Mutate"/>는 보낼 프레임 목록을 반환한다(대부분 1개, <see cref="ModbusSlaveFaultKind.DuplicateResponse"/>만
/// 2개, <see cref="ModbusSlaveFaultKind.NoResponse"/>만 0개).</summary>
public static class ModbusSlaveResponseMutator
{
    public static IReadOnlyList<byte[]> Mutate(byte[] validResponse, ModbusSlaveFaultKind kind)
    {
        switch (kind)
        {
            case ModbusSlaveFaultKind.NoResponse:
                return Array.Empty<byte[]>();

            case ModbusSlaveFaultKind.DelayedResponse:
                return new[] { validResponse }; // 지연 자체는 호출자가 Task.Delay로 처리

            case ModbusSlaveFaultKind.CrcError:
            {
                var frame = (byte[])validResponse.Clone();
                frame[^1] ^= 0xFF;
                return new[] { frame };
            }

            case ModbusSlaveFaultKind.WrongSlaveIdEcho:
            {
                var frame = (byte[])validResponse.Clone();
                frame[0] = (byte)(frame[0] + 1);
                return new[] { RecomputeCrc(frame) };
            }

            case ModbusSlaveFaultKind.WrongFunctionCode:
            {
                var frame = (byte[])validResponse.Clone();
                frame[1] = (byte)((frame[1] + 1) & 0x7F); // Exception 비트는 세우지 않음
                return new[] { RecomputeCrc(frame) };
            }

            case ModbusSlaveFaultKind.WrongByteCount:
            {
                // 읽기 응답(id,fc,byteCount,data...,crc2) 구조 가정 — byteCount만 +2 조작.
                var frame = (byte[])validResponse.Clone();
                if (frame.Length > 3) frame[2] = (byte)(frame[2] + 2);
                return new[] { RecomputeCrc(frame) };
            }

            case ModbusSlaveFaultKind.DataMismatch:
            {
                var frame = (byte[])validResponse.Clone();
                if (frame.Length > 4) frame[3] ^= 0xFF; // 데이터 첫 바이트 변조
                return new[] { RecomputeCrc(frame) };
            }

            case ModbusSlaveFaultKind.PartialFrame:
            {
                var cut = Math.Max(2, validResponse.Length - 2);
                return new[] { validResponse.Take(cut).ToArray() }; // CRC 포함 마지막 일부를 잘라냄(CRC도 깨짐 — 의도됨)
            }

            case ModbusSlaveFaultKind.OversizedFrame:
            {
                var frame = validResponse.Concat(new byte[] { 0xAA, 0xBB }).ToArray();
                return new[] { frame };
            }

            case ModbusSlaveFaultKind.UnexpectedException:
            {
                var slaveId = validResponse[0];
                var fc = (byte)(validResponse[1] & 0x7F);
                return new[] { ModbusSlaveCodec.BuildExceptionResponse(slaveId, fc, 0x04) }; // Slave Device Failure
            }

            case ModbusSlaveFaultKind.DuplicateResponse:
                return new[] { validResponse, validResponse };

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static byte[] RecomputeCrc(byte[] frame)
    {
        var body = frame.AsSpan(0, frame.Length - 2).ToArray();
        var crc = OperateTest.App.Services.Modbus.ModbusRtuCodec.ComputeCrc16(body);
        var result = new byte[frame.Length];
        body.CopyTo(result, 0);
        result[^2] = (byte)(crc & 0xFF);
        result[^1] = (byte)((crc >> 8) & 0xFF);
        return result;
    }
}
