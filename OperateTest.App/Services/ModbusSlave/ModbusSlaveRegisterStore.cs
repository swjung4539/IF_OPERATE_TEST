namespace OperateTest.App.Services.ModbusSlave;

/// <summary>이 에뮬레이터가 흉내내는 하나의 주변장치(Slave)가 가진 레지스터/코일 값 저장소.
/// 주소는 희소(sparse) Dictionary로 관리한다 — DIO/Heating/Gas 폴링 테이블의 주소가 연속적이지
/// 않기 때문이다(예: Heating은 IR1000/IR1018/HR2000처럼 떨어져 있다,
/// `a_poll_table.c` 확인 결과). Read-only 성격의 Input Register/Discrete Input은 Slave 쪽에서
/// 자유롭게 값을 바꿀 수 있게(시뮬레이션 값 주입) 해 두지만, 컨트롤러(Write)가 직접 쓰지는 못한다.</summary>
public sealed class ModbusSlaveRegisterStore
{
    private readonly Dictionary<ushort, ushort> _holding = new();
    private readonly Dictionary<ushort, ushort> _input = new();
    private readonly Dictionary<ushort, bool> _coils = new();
    private readonly Dictionary<ushort, bool> _discreteInputs = new();
    private readonly object _gate = new();

    public byte SlaveId { get; }

    public ModbusSlaveRegisterStore(byte slaveId)
    {
        SlaveId = slaveId;
    }

    public void SeedHolding(ushort startAddress, ushort[] values)
    {
        lock (_gate)
        {
            for (var i = 0; i < values.Length; i++) _holding[(ushort)(startAddress + i)] = values[i];
        }
    }

    public void SeedInput(ushort startAddress, ushort[] values)
    {
        lock (_gate)
        {
            for (var i = 0; i < values.Length; i++) _input[(ushort)(startAddress + i)] = values[i];
        }
    }

    public void SeedCoils(ushort startAddress, bool[] values)
    {
        lock (_gate)
        {
            for (var i = 0; i < values.Length; i++) _coils[(ushort)(startAddress + i)] = values[i];
        }
    }

    public void SeedDiscreteInputs(ushort startAddress, bool[] values)
    {
        lock (_gate)
        {
            for (var i = 0; i < values.Length; i++) _discreteInputs[(ushort)(startAddress + i)] = values[i];
        }
    }

    public ushort[]? TryReadHolding(ushort startAddress, ushort quantity) => TryReadRegisters(_holding, startAddress, quantity);
    public ushort[]? TryReadInput(ushort startAddress, ushort quantity) => TryReadRegisters(_input, startAddress, quantity);

    public bool[]? TryReadCoils(ushort startAddress, ushort quantity) => TryReadBits(_coils, startAddress, quantity);
    public bool[]? TryReadDiscreteInputs(ushort startAddress, ushort quantity) => TryReadBits(_discreteInputs, startAddress, quantity);

    /// <summary>지정 범위가 전부 이미 시딩(구성)돼 있어야 쓸 수 있다 — 구성되지 않은 주소로의 쓰기는
    /// "Illegal Data Address"로 취급한다(false 반환).</summary>
    public bool TryWriteHolding(ushort startAddress, ushort[] values)
    {
        lock (_gate)
        {
            for (var i = 0; i < values.Length; i++)
                if (!_holding.ContainsKey((ushort)(startAddress + i))) return false;
            for (var i = 0; i < values.Length; i++)
                _holding[(ushort)(startAddress + i)] = values[i];
            return true;
        }
    }

    public bool TryWriteCoils(ushort startAddress, bool[] values)
    {
        lock (_gate)
        {
            for (var i = 0; i < values.Length; i++)
                if (!_coils.ContainsKey((ushort)(startAddress + i))) return false;
            for (var i = 0; i < values.Length; i++)
                _coils[(ushort)(startAddress + i)] = values[i];
            return true;
        }
    }

    /// <summary>"이 FC/주소/Quantity 요청에 정상 응답할 수 있는가"를 실제로 쓰거나 읽지 않고 미리
    /// 판단하기 위한 순수 조회(REQUIREMENTS 4절 — 장치 프로파일별 등록 범위 검증에 쓰인다). 읽기 FC는
    /// 해당 테이블에, 쓰기 FC(단일/복수)는
    /// 대상 테이블(Coil은 05/0F, Holding은 06/10)에 범위가 전부 시딩돼 있는지만 확인한다 — 값 자체는
    /// 건드리지 않는다(<see cref="TryWriteHolding"/>/<see cref="TryWriteCoils"/>의 등록 여부 확인 로직과
    /// 동일한 기준을 공유하지만 부작용이 없다).</summary>
    public bool IsRequestRegistered(byte functionCode, ushort startAddress, ushort quantity)
    {
        lock (_gate)
        {
            return functionCode switch
            {
                0x01 or 0x05 or 0x0F => RangeExists(_coils, startAddress, quantity),
                0x02 => RangeExists(_discreteInputs, startAddress, quantity),
                0x03 or 0x06 or 0x10 => RangeExists(_holding, startAddress, quantity),
                0x04 => RangeExists(_input, startAddress, quantity),
                _ => false
            };
        }
    }

    private static bool RangeExists<T>(Dictionary<ushort, T> table, ushort startAddress, ushort quantity)
    {
        for (var i = 0; i < quantity; i++)
            if (!table.ContainsKey((ushort)(startAddress + i))) return false;
        return true;
    }

    /// <summary>미등록 주소 오류 상세("등록된 Map 범위")와 "이 대상이 응답 가능한 요청"(REQUIREMENTS
    /// 4절, 4.6번 "어떤 FC·주소 요청을 기다리는지 명시")에 쓸 사람이 읽을 수 있는 요약 — 실제 요청
    /// 범위와 나란히 보여줘서 어디가 안 맞는지 바로 비교할 수 있게 한다. 읽기 FC(01/02/03/04)를
    /// 앞에 붙여 어떤 요청을 가리키는지 바로 알 수 있게 한다(쓰기 FC는 같은 주소를 공유하므로 별도
    /// 표기하지 않는다).</summary>
    public string DescribeRegisteredRanges()
    {
        lock (_gate)
        {
            var parts = new List<string>();
            AppendRanges(parts, "FC01 Coil", _coils.Keys);
            AppendRanges(parts, "FC02 DiscreteInput", _discreteInputs.Keys);
            AppendRanges(parts, "FC03 Holding", _holding.Keys);
            AppendRanges(parts, "FC04 Input", _input.Keys);
            return parts.Count == 0 ? "(등록된 주소 없음)" : string.Join(", ", parts);
        }
    }

    private static void AppendRanges(List<string> parts, string label, IEnumerable<ushort> keys)
    {
        var sorted = keys.Distinct().OrderBy(k => k).ToArray();
        var i = 0;
        while (i < sorted.Length)
        {
            var start = sorted[i];
            var end = start;
            while (i + 1 < sorted.Length && sorted[i + 1] == end + 1) { i++; end = sorted[i]; }
            parts.Add(start == end ? $"{label} {start}" : $"{label} {start}-{end}");
            i++;
        }
    }

    private ushort[]? TryReadRegisters(Dictionary<ushort, ushort> table, ushort startAddress, ushort quantity)
    {
        lock (_gate)
        {
            var result = new ushort[quantity];
            for (var i = 0; i < quantity; i++)
            {
                if (!table.TryGetValue((ushort)(startAddress + i), out var v)) return null;
                result[i] = v;
            }
            return result;
        }
    }

    private bool[]? TryReadBits(Dictionary<ushort, bool> table, ushort startAddress, ushort quantity)
    {
        lock (_gate)
        {
            var result = new bool[quantity];
            for (var i = 0; i < quantity; i++)
            {
                if (!table.TryGetValue((ushort)(startAddress + i), out var v)) return null;
                result[i] = v;
            }
            return result;
        }
    }

    /// <summary>Slave Map 저장(OPEN_ITEMS_AUDIT.md OI-34)을 위한 스냅샷/복원 — 이제 Slave Emulator가
    /// 실제로 존재하므로 <c>SlaveMapSettings</c>가 예약 플레이스홀더가 아니라 이 스냅샷을 담을 수 있다.</summary>
    public ModbusSlaveRegisterSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ModbusSlaveRegisterSnapshot(
                SlaveId,
                new Dictionary<ushort, ushort>(_holding),
                new Dictionary<ushort, ushort>(_input),
                new Dictionary<ushort, bool>(_coils),
                new Dictionary<ushort, bool>(_discreteInputs));
        }
    }

    public static ModbusSlaveRegisterStore Restore(ModbusSlaveRegisterSnapshot snapshot)
    {
        var store = new ModbusSlaveRegisterStore(snapshot.SlaveId);
        lock (store._gate)
        {
            foreach (var kv in snapshot.Holding) store._holding[kv.Key] = kv.Value;
            foreach (var kv in snapshot.Input) store._input[kv.Key] = kv.Value;
            foreach (var kv in snapshot.Coils) store._coils[kv.Key] = kv.Value;
            foreach (var kv in snapshot.DiscreteInputs) store._discreteInputs[kv.Key] = kv.Value;
        }
        return store;
    }

    /// <summary>DIO/Heating/Gas(H2 기본형) 내부 폴링 테이블 주소로 초기화한 기본 저장소.
    /// 주소는 `a_poll_table.c`(DIO: HR129/130, Heating: IR1000/1018/HR2000-2003,
    /// Gas H2: IR30000/DI10001/Coil2·3)에서 확인한 실제 주소이며, 값은 전부 0(안전측 기본값)이다 —
    /// 값 자체의 실제 의미(온도/유량 등)는 IF_DEVICE_PROFILE_SPEC.md에도 없어 추측하지 않는다.</summary>
    /// <summary>gasProfile은 <paramref name="bus"/>가 <see cref="ModbusSlaveBus.Usart3Gas"/>일 때만
    /// 의미가 있다(REQUIREMENTS 4절, <see cref="GasDetectorProfile"/> 참고) — 다른 버스는 무시한다.</summary>
    public static ModbusSlaveRegisterStore CreateDefaultFor(ModbusSlaveBus bus, byte slaveId, GasDetectorProfile gasProfile = GasDetectorProfile.Std)
    {
        var store = new ModbusSlaveRegisterStore(slaveId);
        switch (bus)
        {
            case ModbusSlaveBus.Lpuart1Dio:
                store.SeedHolding(130, new ushort[1]); // poll: FC03 HR130 qty1
                store.SeedHolding(129, new ushort[1]); // write: FC10 HR129 qty1
                break;
            case ModbusSlaveBus.Usart2Heating:
                store.SeedInput(1000, new ushort[18]);   // poll: FC04 IR1000 qty18
                store.SeedInput(1018, new ushort[18]);   // poll: FC04 IR1018 qty18
                store.SeedHolding(2000, new ushort[21]); // poll: FC03 HR2000 qty21
                store.SeedHolding(2003, new ushort[18]); // write-only: FC10 HR2003 qty18
                break;
            case ModbusSlaveBus.Usart3Gas:
                switch (gasProfile)
                {
                    case GasDetectorProfile.Std:
                        store.SeedInput(0, new ushort[4]);          // wire poll: FC04 address 0 qty4 (logical IR30000)
                        store.SeedDiscreteInputs(0, new bool[8]); // wire poll: FC02 address 0 qty8 (logical DI10001)
                        store.SeedCoils(2, new bool[2]);              // poll: FC01 Coil2 qty2, write: FC05/FC0F Coil2/3
                        store.SeedCoils(3, new bool[1]);
                        break;
                    case GasDetectorProfile.Tft:
                        // 현재 MAIN a_poll_table.c의 H2 TFT FC01/02/03/04 범위를 그대로 등록한다.
                        store.SeedCoils(1, new bool[4]);           // poll: FC01 Coil1 qty4
                        store.SeedDiscreteInputs(0, new bool[16]);
                        store.SeedInput(1, new ushort[15]);
                        store.SeedInput(21, new ushort[15]);
                        store.SeedInput(85, new ushort[2]);
                        store.SeedInput(89, new ushort[1]);
                        store.SeedInput(91, new ushort[6]);
                        store.SeedHolding(1, new ushort[15]);
                        store.SeedHolding(21, new ushort[15]);
                        store.SeedHolding(85, new ushort[2]);
                        store.SeedHolding(89, new ushort[1]);
                        store.SeedHolding(91, new ushort[6]);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(gasProfile));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(bus));
        }
        return store;
    }
}

/// <summary>저장/복원 가능한 레지스터 스냅샷 하나(설정 파일에 그대로 직렬화됨).</summary>
public sealed record ModbusSlaveRegisterSnapshot(
    byte SlaveId,
    Dictionary<ushort, ushort> Holding,
    Dictionary<ushort, ushort> Input,
    Dictionary<ushort, bool> Coils,
    Dictionary<ushort, bool> DiscreteInputs);
