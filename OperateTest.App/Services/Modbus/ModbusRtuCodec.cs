namespace OperateTest.App.Services.Modbus;

/// <summary>
/// Modbus RTU 프레임 생성, CRC16 계산, 응답 파싱/검증만 담당하는 순수 함수 모음. I/O를 전혀 하지
/// 않으며(byte[] in, byte[]/예외 out), 그래서 실제 포트 없이도 합성한 바이트 배열로 그대로
/// 단위 검증할 수 있다. CRC16은 COMMUNICATION_SPEC.md 3절에서 확인한 IF_main 코드와 동일한 알고리즘
/// (다항식 0xA001, 초기값 0xFFFF, 결과는 Lo 바이트 먼저 부착)이다.
/// </summary>
public static class ModbusRtuCodec
{
    public static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc >>= 1;
                    crc ^= 0xA001;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        return crc;
    }

    private static void AppendCrc(List<byte> frame)
    {
        var crc = ComputeCrc16(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(frame));
        frame.Add((byte)(crc & 0xFF));       // CRC Lo
        frame.Add((byte)((crc >> 8) & 0xFF)); // CRC Hi
    }

    /// <summary>FC03(Read Holding Registers)/FC04(Read Input Registers) 공용 요청 프레임.</summary>
    public static byte[] BuildReadRequest(byte slaveId, byte functionCode, ushort startAddress, ushort quantity)
    {
        var frame = new List<byte>(8)
        {
            slaveId,
            functionCode,
            (byte)(startAddress >> 8), (byte)(startAddress & 0xFF),
            (byte)(quantity >> 8), (byte)(quantity & 0xFF)
        };
        AppendCrc(frame);
        return frame.ToArray();
    }

    /// <summary>FC06(Write Single Register) 요청 프레임.</summary>
    public static byte[] BuildWriteSingleRegisterRequest(byte slaveId, ushort address, ushort value)
    {
        var frame = new List<byte>(8)
        {
            slaveId,
            ModbusFunctionCode.WriteSingleRegister,
            (byte)(address >> 8), (byte)(address & 0xFF),
            (byte)(value >> 8), (byte)(value & 0xFF)
        };
        AppendCrc(frame);
        return frame.ToArray();
    }

    /// <summary>FC10(Write Multiple Registers) 요청 프레임.</summary>
    public static byte[] BuildWriteMultipleRegistersRequest(byte slaveId, ushort startAddress, ushort[] values)
    {
        if (values.Length == 0 || values.Length > 123)
            throw new ArgumentOutOfRangeException(nameof(values), "FC10 수량은 1~123개여야 합니다.");

        var byteCount = (byte)(values.Length * 2);
        var frame = new List<byte>(7 + byteCount)
        {
            slaveId,
            ModbusFunctionCode.WriteMultipleRegisters,
            (byte)(startAddress >> 8), (byte)(startAddress & 0xFF),
            (byte)(values.Length >> 8), (byte)(values.Length & 0xFF),
            byteCount
        };
        foreach (var v in values)
        {
            frame.Add((byte)(v >> 8));
            frame.Add((byte)(v & 0xFF));
        }
        AppendCrc(frame);
        return frame.ToArray();
    }

    /// <summary>FC01(Read Coils)/FC02(Read Discrete Inputs) 공용 요청 프레임. 프레임 구조가
    /// FC03/FC04 읽기 요청과 동일해 <see cref="BuildReadRequest"/>를 그대로 재사용한다.</summary>
    public static byte[] BuildReadBitsRequest(byte slaveId, byte functionCode, ushort startAddress, ushort quantity)
        => BuildReadRequest(slaveId, functionCode, startAddress, quantity);

    /// <summary>FC05(Write Single Coil) 요청 프레임. 코일 값은 표준 Modbus 규약대로 ON=0xFF00,
    /// OFF=0x0000으로 인코딩한다(IF_main <c>a_modbus_master.c:279</c>의
    /// <c>(pending->values[0] != 0U) ? 0xFF00U : 0x0000U</c>와 동일한 규약).</summary>
    public static byte[] BuildWriteSingleCoilRequest(byte slaveId, ushort address, bool value)
    {
        var coilValue = value ? (ushort)0xFF00 : (ushort)0x0000;
        var frame = new List<byte>(8)
        {
            slaveId,
            ModbusFunctionCode.WriteSingleCoil,
            (byte)(address >> 8), (byte)(address & 0xFF),
            (byte)(coilValue >> 8), (byte)(coilValue & 0xFF)
        };
        AppendCrc(frame);
        return frame.ToArray();
    }

    /// <summary>FC0F(Write Multiple Coils) 요청 프레임. Bit 순서는 첫 코일이 첫 데이터 바이트의
    /// 비트0(LSB)에 오는 표준 순서(IF_main <c>a_modbus_master.c:312</c>,
    /// <c>a_plc_group_map.c:115</c>의 <c>1U &lt;&lt; (offset % 8)</c> 패킹과 동일)를 따른다.</summary>
    public static byte[] BuildWriteMultipleCoilsRequest(byte slaveId, ushort startAddress, bool[] values)
    {
        if (values.Length == 0 || values.Length > 1968)
            throw new ArgumentOutOfRangeException(nameof(values), "FC0F 수량은 1~1968개여야 합니다.");

        var byteCount = (byte)((values.Length + 7) / 8);
        var frame = new List<byte>(7 + byteCount)
        {
            slaveId,
            ModbusFunctionCode.WriteMultipleCoils,
            (byte)(startAddress >> 8), (byte)(startAddress & 0xFF),
            (byte)(values.Length >> 8), (byte)(values.Length & 0xFF),
            byteCount
        };

        var packed = new byte[byteCount];
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i])
                packed[i / 8] |= (byte)(1 << (i % 8));
        }
        frame.AddRange(packed);

        AppendCrc(frame);
        return frame.ToArray();
    }

    /// <summary>응답 프레임에서 CRC를 검증한다(전체 길이 기준 마지막 2바이트를 트레일러로 본다).
    /// 실패하면 <see cref="ModbusCrcMismatchException"/>을 던진다.</summary>
    private static void VerifyCrc(byte slaveId, byte functionCode, byte[] response)
    {
        var payload = response.AsSpan(0, response.Length - 2);
        var expected = ComputeCrc16(payload);
        var actual = (ushort)(response[^2] | (response[^1] << 8));
        if (expected != actual)
            throw new ModbusCrcMismatchException(slaveId, functionCode, expected, actual);
    }

    /// <summary>FC03/FC04 응답을 검증하고 레지스터 값을 파싱한다. 검증 순서: 길이 최소치 →
    /// CRC → Slave ID → Function Code(Exception 포함) → Byte Count.</summary>
    public static ModbusReadResult ParseReadResponse(byte slaveId, byte functionCode, ushort startAddress, ushort quantity, byte[] response)
    {
        if (response.Length < 5)
            throw new ModbusByteCountMismatchException(slaveId, functionCode, $"응답 길이가 {response.Length}바이트로 최소 5바이트에 못 미칩니다.");

        VerifyCrc(slaveId, functionCode, response);

        if (response[0] != slaveId)
            throw new ModbusSlaveIdMismatchException(slaveId, functionCode, response[0]);

        if (response[1] == (byte)(functionCode | 0x80))
        {
            if (response.Length != 5)
                throw new ModbusByteCountMismatchException(slaveId, functionCode, $"Exception 응답 길이가 {response.Length}바이트로 5바이트가 아닙니다.");
            throw new ModbusExceptionResponseException(slaveId, functionCode, response[2]);
        }

        if (response[1] != functionCode)
            throw new ModbusFunctionCodeMismatchException(slaveId, functionCode, response[1]);

        var byteCount = response[2];
        var expectedByteCount = quantity * 2;
        var expectedLength = 3 + expectedByteCount + 2;
        if (byteCount != expectedByteCount || response.Length != expectedLength)
            throw new ModbusByteCountMismatchException(slaveId, functionCode,
                $"byteCount={byteCount}(기대 {expectedByteCount}), 전체 길이={response.Length}(기대 {expectedLength})");

        var registers = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
        {
            registers[i] = (ushort)((response[3 + i * 2] << 8) | response[3 + i * 2 + 1]);
        }

        return new ModbusReadResult(slaveId, functionCode, startAddress, registers, response);
    }

    /// <summary>FC06 응답을 검증한다. 정상 응답은 요청 프레임을 그대로 echo해야 한다. 검증 순서:
    /// 길이 → CRC → Slave ID → Function Code(Exception 포함) → Write Echo(주소/값).</summary>
    public static ModbusWriteResult ParseWriteSingleRegisterResponse(byte slaveId, ushort address, ushort value, byte[] response)
    {
        const byte fc = ModbusFunctionCode.WriteSingleRegister;

        if (response.Length < 5)
            throw new ModbusByteCountMismatchException(slaveId, fc, $"응답 길이가 {response.Length}바이트로 최소 5바이트에 못 미칩니다.");

        VerifyCrc(slaveId, fc, response);

        if (response[0] != slaveId)
            throw new ModbusSlaveIdMismatchException(slaveId, fc, response[0]);

        if (response[1] == (byte)(fc | 0x80))
        {
            if (response.Length != 5)
                throw new ModbusByteCountMismatchException(slaveId, fc, $"Exception 응답 길이가 {response.Length}바이트로 5바이트가 아닙니다.");
            throw new ModbusExceptionResponseException(slaveId, fc, response[2]);
        }

        if (response[1] != fc)
            throw new ModbusFunctionCodeMismatchException(slaveId, fc, response[1]);

        if (response.Length != 8)
            throw new ModbusWriteEchoMismatchException(slaveId, fc, $"응답 길이 {response.Length}바이트(기대 8바이트)");

        var echoAddress = (ushort)((response[2] << 8) | response[3]);
        var echoValue = (ushort)((response[4] << 8) | response[5]);
        if (echoAddress != address || echoValue != value)
            throw new ModbusWriteEchoMismatchException(slaveId, fc,
                $"요청(주소 {address}, 값 {value}) vs 응답(주소 {echoAddress}, 값 {echoValue})");

        return new ModbusWriteResult(slaveId, fc, address, value, response);
    }

    /// <summary>FC10 응답을 검증한다. 정상 응답은 데이터 없이 시작주소+수량만 echo한다. 검증 순서는
    /// FC06과 동일하다.</summary>
    public static ModbusWriteResult ParseWriteMultipleRegistersResponse(byte slaveId, ushort startAddress, int quantity, byte[] response)
    {
        const byte fc = ModbusFunctionCode.WriteMultipleRegisters;

        if (response.Length < 5)
            throw new ModbusByteCountMismatchException(slaveId, fc, $"응답 길이가 {response.Length}바이트로 최소 5바이트에 못 미칩니다.");

        VerifyCrc(slaveId, fc, response);

        if (response[0] != slaveId)
            throw new ModbusSlaveIdMismatchException(slaveId, fc, response[0]);

        if (response[1] == (byte)(fc | 0x80))
        {
            if (response.Length != 5)
                throw new ModbusByteCountMismatchException(slaveId, fc, $"Exception 응답 길이가 {response.Length}바이트로 5바이트가 아닙니다.");
            throw new ModbusExceptionResponseException(slaveId, fc, response[2]);
        }

        if (response[1] != fc)
            throw new ModbusFunctionCodeMismatchException(slaveId, fc, response[1]);

        if (response.Length != 8)
            throw new ModbusWriteEchoMismatchException(slaveId, fc, $"응답 길이 {response.Length}바이트(기대 8바이트)");

        var echoAddress = (ushort)((response[2] << 8) | response[3]);
        var echoQuantity = (response[4] << 8) | response[5];
        if (echoAddress != startAddress || echoQuantity != quantity)
            throw new ModbusWriteEchoMismatchException(slaveId, fc,
                $"요청(시작주소 {startAddress}, 수량 {quantity}) vs 응답(시작주소 {echoAddress}, 수량 {echoQuantity})");

        return new ModbusWriteResult(slaveId, fc, startAddress, (ushort)quantity, response);
    }

    /// <summary>FC01/FC02 응답을 검증하고 Bit 값을 파싱한다. 검증 순서는 FC03/FC04와 같되(길이 →
    /// CRC → Slave ID → Function Code(Exception 포함) → Byte Count), Byte Count 기대값은
    /// <c>ceil(quantity/8)</c>이고 데이터는 레지스터가 아니라 Bit 배열로 해석한다. Bit 순서는 첫
    /// 코일/입력이 첫 데이터 바이트의 비트0(LSB)에 오는 표준 순서다.</summary>
    public static ModbusBitReadResult ParseReadBitsResponse(byte slaveId, byte functionCode, ushort startAddress, ushort quantity, byte[] response)
    {
        if (response.Length < 5)
            throw new ModbusByteCountMismatchException(slaveId, functionCode, $"응답 길이가 {response.Length}바이트로 최소 5바이트에 못 미칩니다.");

        VerifyCrc(slaveId, functionCode, response);

        if (response[0] != slaveId)
            throw new ModbusSlaveIdMismatchException(slaveId, functionCode, response[0]);

        if (response[1] == (byte)(functionCode | 0x80))
        {
            if (response.Length != 5)
                throw new ModbusByteCountMismatchException(slaveId, functionCode, $"Exception 응답 길이가 {response.Length}바이트로 5바이트가 아닙니다.");
            throw new ModbusExceptionResponseException(slaveId, functionCode, response[2]);
        }

        if (response[1] != functionCode)
            throw new ModbusFunctionCodeMismatchException(slaveId, functionCode, response[1]);

        var byteCount = response[2];
        var expectedByteCount = (quantity + 7) / 8;
        var expectedLength = 3 + expectedByteCount + 2;
        if (byteCount != expectedByteCount || response.Length != expectedLength)
            throw new ModbusByteCountMismatchException(slaveId, functionCode,
                $"byteCount={byteCount}(기대 {expectedByteCount}), 전체 길이={response.Length}(기대 {expectedLength})");

        var bits = new bool[quantity];
        for (var i = 0; i < quantity; i++)
        {
            var byteIndex = 3 + i / 8;
            var bitIndex = i % 8;
            bits[i] = (response[byteIndex] & (1 << bitIndex)) != 0;
        }

        return new ModbusBitReadResult(slaveId, functionCode, startAddress, bits, response);
    }

    /// <summary>FC05 응답을 검증한다. 정상 응답은 요청 프레임을 그대로 echo해야 한다(주소 +
    /// 0xFF00/0x0000 값). 검증 순서는 FC06과 동일하다.</summary>
    public static ModbusWriteResult ParseWriteSingleCoilResponse(byte slaveId, ushort address, bool value, byte[] response)
    {
        const byte fc = ModbusFunctionCode.WriteSingleCoil;

        if (response.Length < 5)
            throw new ModbusByteCountMismatchException(slaveId, fc, $"응답 길이가 {response.Length}바이트로 최소 5바이트에 못 미칩니다.");

        VerifyCrc(slaveId, fc, response);

        if (response[0] != slaveId)
            throw new ModbusSlaveIdMismatchException(slaveId, fc, response[0]);

        if (response[1] == (byte)(fc | 0x80))
        {
            if (response.Length != 5)
                throw new ModbusByteCountMismatchException(slaveId, fc, $"Exception 응답 길이가 {response.Length}바이트로 5바이트가 아닙니다.");
            throw new ModbusExceptionResponseException(slaveId, fc, response[2]);
        }

        if (response[1] != fc)
            throw new ModbusFunctionCodeMismatchException(slaveId, fc, response[1]);

        if (response.Length != 8)
            throw new ModbusWriteEchoMismatchException(slaveId, fc, $"응답 길이 {response.Length}바이트(기대 8바이트)");

        var expectedValue = value ? (ushort)0xFF00 : (ushort)0x0000;
        var echoAddress = (ushort)((response[2] << 8) | response[3]);
        var echoValue = (ushort)((response[4] << 8) | response[5]);
        if (echoAddress != address || echoValue != expectedValue)
            throw new ModbusWriteEchoMismatchException(slaveId, fc,
                $"요청(주소 {address}, 값 0x{expectedValue:X4}) vs 응답(주소 {echoAddress}, 값 0x{echoValue:X4})");

        return new ModbusWriteResult(slaveId, fc, address, echoValue, response);
    }

    /// <summary>FC0F 응답을 검증한다. 정상 응답은 데이터 없이 시작주소+수량만 echo한다(FC10과 동일한
    /// 응답 구조). 검증 순서는 FC06/FC10과 동일하다.</summary>
    public static ModbusWriteResult ParseWriteMultipleCoilsResponse(byte slaveId, ushort startAddress, int quantity, byte[] response)
    {
        const byte fc = ModbusFunctionCode.WriteMultipleCoils;

        if (response.Length < 5)
            throw new ModbusByteCountMismatchException(slaveId, fc, $"응답 길이가 {response.Length}바이트로 최소 5바이트에 못 미칩니다.");

        VerifyCrc(slaveId, fc, response);

        if (response[0] != slaveId)
            throw new ModbusSlaveIdMismatchException(slaveId, fc, response[0]);

        if (response[1] == (byte)(fc | 0x80))
        {
            if (response.Length != 5)
                throw new ModbusByteCountMismatchException(slaveId, fc, $"Exception 응답 길이가 {response.Length}바이트로 5바이트가 아닙니다.");
            throw new ModbusExceptionResponseException(slaveId, fc, response[2]);
        }

        if (response[1] != fc)
            throw new ModbusFunctionCodeMismatchException(slaveId, fc, response[1]);

        if (response.Length != 8)
            throw new ModbusWriteEchoMismatchException(slaveId, fc, $"응답 길이 {response.Length}바이트(기대 8바이트)");

        var echoAddress = (ushort)((response[2] << 8) | response[3]);
        var echoQuantity = (response[4] << 8) | response[5];
        if (echoAddress != startAddress || echoQuantity != quantity)
            throw new ModbusWriteEchoMismatchException(slaveId, fc,
                $"요청(시작주소 {startAddress}, 수량 {quantity}) vs 응답(시작주소 {echoAddress}, 수량 {echoQuantity})");

        return new ModbusWriteResult(slaveId, fc, startAddress, (ushort)quantity, response);
    }

    /// <summary>수신 누적 중 아직 완전한 프레임인지 판단하기 위해 필요한 예상 전체 길이를 계산한다.
    /// 최소 2바이트(id+fc)가 있어야 Exception/Write 여부를 알 수 있고, 읽기 응답(FC01/02/03/04)은
    /// byteCount(3번째 바이트)까지 있어야 계산할 수 있다. 아직 판단할 수 없으면 null. 이 로직은
    /// function code별로 분기하지 않는 일반 규칙이라 FC01/FC02(읽기, byteCount 필드 위치가 FC03/04와
    /// 동일)와 FC05/FC0F(쓰기 echo, FC06/FC10과 동일한 8바이트 구조)를 추가할 때도 수정이 필요
    /// 없었다.</summary>
    public static int? TryComputeExpectedLength(byte functionCode, bool isWriteRequest, IReadOnlyList<byte> received)
    {
        if (received.Count < 2) return null;

        if (received[1] == (byte)(functionCode | 0x80))
            return 5; // Exception 응답은 항상 5바이트

        if (isWriteRequest)
            return 8; // FC05/FC06/FC0F/FC10 정상 echo 응답은 항상 8바이트

        // FC01/02/03/04 정상 응답: byteCount 필드(3번째 바이트)까지 받아야 전체 길이를 알 수 있다.
        if (received.Count < 3) return null;
        return 3 + received[2] + 2;
    }
}
