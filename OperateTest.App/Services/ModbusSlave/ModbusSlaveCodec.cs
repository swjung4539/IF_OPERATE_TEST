using OperateTest.App.Services.Modbus;

namespace OperateTest.App.Services.ModbusSlave;

/// <summary>Slave(응답자) 관점의 프레임 파싱/생성 순수 함수 모음 — 요청을 만들고 응답을 파싱하는
/// <see cref="ModbusRtuCodec"/>(Master 관점, 이미 검증·완료된 코드)와 반대 방향이라 별도 파일로
/// 분리했다(기존 Master 코드는 전혀 건드리지 않는다). CRC 계산은 동일한 알고리즘이라
/// <see cref="ModbusRtuCodec.ComputeCrc16"/>을 그대로 재사용한다.</summary>
public static class ModbusSlaveCodec
{
    private static readonly HashSet<byte> ReadFunctionCodes = new() { 0x01, 0x02, 0x03, 0x04 };
    private static readonly HashSet<byte> WriteSingleFunctionCodes = new() { 0x05, 0x06 };
    private static readonly HashSet<byte> WriteMultipleFunctionCodes = new() { 0x0F, 0x10 };

    /// <summary>수신 누적 버퍼에서 요청 프레임의 예상 전체 길이를 계산한다. Modbus RTU 요청 프레임은
    /// 응답과 달리 구조가 Function Code만으로 거의 결정된다: 읽기(01/02/03/04)와 단일 쓰기(05/06)는
    /// 항상 8바이트, 복수 쓰기(0F/10)는 byteCount(7번째 바이트)까지 봐야 한다. 알 수 없는 FC는 null —
    /// 호출자가 프레임 사이 무입력 구간(gap)으로 종료를 판단한다.</summary>
    public static int? TryComputeExpectedRequestLength(IReadOnlyList<byte> received)
    {
        if (received.Count < 2) return null;
        var fc = received[1];

        if (ReadFunctionCodes.Contains(fc) || WriteSingleFunctionCodes.Contains(fc))
            return 8;

        if (WriteMultipleFunctionCodes.Contains(fc))
        {
            if (received.Count < 7) return null;
            return 7 + received[6] + 2;
        }

        return null; // 지원하지 않는 FC — gap 기반 종료에 맡긴다.
    }

    /// <summary>CRC가 맞지 않으면 null을 반환한다(표준 Modbus RTU 관례상 잘못된 CRC 프레임은 그냥
    /// 버려지고 예외적인 상황으로 취급하지 않는다 — 응답도 하지 않는다).</summary>
    public static ModbusSlaveRequest? TryParse(byte[] frame)
    {
        if (frame.Length < 4) return null;

        var payload = frame.AsSpan(0, frame.Length - 2);
        var expectedCrc = ModbusRtuCodec.ComputeCrc16(payload);
        var actualCrc = (ushort)(frame[^2] | (frame[^1] << 8));
        if (expectedCrc != actualCrc) return null;

        var slaveId = frame[0];
        var fc = frame[1];

        if (ReadFunctionCodes.Contains(fc))
        {
            if (frame.Length != 8) return null;
            var address = (ushort)((frame[2] << 8) | frame[3]);
            var quantity = (ushort)((frame[4] << 8) | frame[5]);
            return new ModbusSlaveRequest(slaveId, fc, address, quantity, null, null, frame);
        }

        if (fc == 0x06) // Write Single Register
        {
            if (frame.Length != 8) return null;
            var address = (ushort)((frame[2] << 8) | frame[3]);
            var value = (ushort)((frame[4] << 8) | frame[5]);
            return new ModbusSlaveRequest(slaveId, fc, address, 1, new[] { value }, null, frame);
        }

        if (fc == 0x05) // Write Single Coil
        {
            if (frame.Length != 8) return null;
            var address = (ushort)((frame[2] << 8) | frame[3]);
            var raw = (ushort)((frame[4] << 8) | frame[5]);
            return new ModbusSlaveRequest(slaveId, fc, address, 1, null, new[] { raw == 0xFF00 }, frame);
        }

        if (fc == 0x10) // Write Multiple Registers
        {
            var address = (ushort)((frame[2] << 8) | frame[3]);
            var quantity = (ushort)((frame[4] << 8) | frame[5]);
            var byteCount = frame[6];
            if (byteCount != quantity * 2 || frame.Length != 7 + byteCount + 2) return null;
            var values = new ushort[quantity];
            for (var i = 0; i < quantity; i++)
                values[i] = (ushort)((frame[7 + i * 2] << 8) | frame[7 + i * 2 + 1]);
            return new ModbusSlaveRequest(slaveId, fc, address, quantity, values, null, frame);
        }

        if (fc == 0x0F) // Write Multiple Coils
        {
            var address = (ushort)((frame[2] << 8) | frame[3]);
            var quantity = (ushort)((frame[4] << 8) | frame[5]);
            var byteCount = frame[6];
            var expectedByteCount = (quantity + 7) / 8;
            if (byteCount != expectedByteCount || frame.Length != 7 + byteCount + 2) return null;
            var bits = new bool[quantity];
            for (var i = 0; i < quantity; i++)
                bits[i] = (frame[7 + i / 8] & (1 << (i % 8))) != 0;
            return new ModbusSlaveRequest(slaveId, fc, address, quantity, null, bits, frame);
        }

        return null; // 지원하지 않는 FC — Illegal Function 처리는 호출자(Emulator 서비스)가 담당.
    }

    private static void AppendCrc(List<byte> frame)
    {
        var crc = ModbusRtuCodec.ComputeCrc16(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(frame));
        frame.Add((byte)(crc & 0xFF));
        frame.Add((byte)((crc >> 8) & 0xFF));
    }

    public static byte[] BuildReadRegistersResponse(byte slaveId, byte functionCode, ushort[] values)
    {
        var frame = new List<byte> { slaveId, functionCode, (byte)(values.Length * 2) };
        foreach (var v in values)
        {
            frame.Add((byte)(v >> 8));
            frame.Add((byte)(v & 0xFF));
        }
        AppendCrc(frame);
        return frame.ToArray();
    }

    public static byte[] BuildReadBitsResponse(byte slaveId, byte functionCode, bool[] bits)
    {
        var byteCount = (byte)((bits.Length + 7) / 8);
        var frame = new List<byte> { slaveId, functionCode, byteCount };
        var packed = new byte[byteCount];
        for (var i = 0; i < bits.Length; i++)
            if (bits[i]) packed[i / 8] |= (byte)(1 << (i % 8));
        frame.AddRange(packed);
        AppendCrc(frame);
        return frame.ToArray();
    }

    /// <summary>FC05/06/0F/10 정상 echo 응답(요청의 주소+값/수량 필드를 그대로 되돌려줌).</summary>
    public static byte[] BuildWriteEchoResponse(byte slaveId, byte functionCode, ushort address, ushort valueOrQuantity)
    {
        var frame = new List<byte>
        {
            slaveId, functionCode,
            (byte)(address >> 8), (byte)(address & 0xFF),
            (byte)(valueOrQuantity >> 8), (byte)(valueOrQuantity & 0xFF)
        };
        AppendCrc(frame);
        return frame.ToArray();
    }

    public static byte[] BuildExceptionResponse(byte slaveId, byte functionCode, byte exceptionCode)
    {
        var frame = new List<byte> { slaveId, (byte)(functionCode | 0x80), exceptionCode };
        AppendCrc(frame);
        return frame.ToArray();
    }
}
