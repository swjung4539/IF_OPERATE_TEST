using OperateTest.App.Services.Serial;

namespace OperateTest.App.Services.ModbusSlave;

/// <summary><see cref="IModbusSlaveEmulatorService"/>의 실제 구현. <see cref="ISerialPortService"/>
/// 하나만 직접 잡고 raw 송수신하며, 프레임 파싱/생성/변형은 <see cref="ModbusSlaveCodec"/>/
/// <see cref="ModbusSlaveResponseMutator"/>(둘 다 순수 함수)에 위임한다.</summary>
public sealed class ModbusSlaveEmulatorService : IModbusSlaveEmulatorService
{
    private const int FrameGapMs = 20;
    private const int MaxRawFrameLength = 256;
    private const int MaxExchangeHistory = 20_000;

    private readonly ISerialPortService _serial;
    private readonly Func<int, CancellationToken, Task> _delayAsync;
    private readonly ModbusSlaveRegisterStore _store;
    private readonly IReadOnlyDictionary<byte, ModbusSlaveRegisterStore> _busStores;
    private readonly List<ModbusSlaveExchange> _exchanges = new();
    private readonly object _gate = new();

    /// <summary>영구 RX 버퍼 — <see cref="ReceiveRequestFrameAsync"/> 호출 사이에 남는 바이트를 여기
    /// 보관한다. 이전엔 한 번의 저수준 Receive 청크에 프레임이 2개 이상 들어와도(라운드로빈 버스에서
    /// 우리 요청 바로 뒤에 다른 Slave ID 요청이 실려오는 경우가 흔하다) 첫 프레임 길이만큼만 잘라
    /// 반환하고 나머지는 그 호출이 끝나며 통째로 버려졌다 — 그 뒤에 붙어온, 실제로는 우리에게 온
    /// 정상 요청이었을 수도 있는 프레임까지 함께 유실됐다. 매 호출이 로컬 List를 새로 만드는 대신
    /// 이 인스턴스 필드에 이어 쓰고, 한 프레임을 꺼낸 뒤 남는 바이트는 여기 그대로 둔다.</summary>
    private readonly List<byte> _rxBuffer = new();

    public ModbusSlaveEmulatorService(
        ISerialPortService serial,
        ModbusSlaveRegisterStore store,
        Func<int, CancellationToken, Task>? delayAsync = null)
        : this(serial, store, new[] { store }, delayAsync)
    {
    }

    /// <summary>선택 대상뿐 아니라 같은 물리 버스의 형제 Slave store도 함께 받는다. PC는 실제로
    /// Multi-Slave 장비 역할을 하므로, 대상 Fault를 기다리는 동안 형제 ID의 polling 요청에는 정상
    /// 응답해야 컨트롤러의 순차 polling이 timeout 재시도에 갇히지 않는다.</summary>
    public ModbusSlaveEmulatorService(
        ISerialPortService serial,
        ModbusSlaveRegisterStore store,
        IEnumerable<ModbusSlaveRegisterStore> busStores,
        Func<int, CancellationToken, Task>? delayAsync = null)
    {
        _serial = serial;
        _delayAsync = delayAsync ?? ((delayMs, ct) => Task.Delay(delayMs, ct));
        _store = store;
        _busStores = busStores
            .Append(store)
            .GroupBy(candidate => candidate.SlaveId)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    public IReadOnlyList<ModbusSlaveExchange> Exchanges { get { lock (_gate) return _exchanges.ToList(); } }
    public event EventHandler<ModbusSlaveExchange>? ExchangeRecorded;

    public void ClearExchanges()
    {
        lock (_gate) { _exchanges.Clear(); }
    }

    /// <summary>같은 내부 버스(USART2/USART3 등)에는 이 에뮬레이터가 맡은 Slave ID 하나 말고도
    /// 실제로는 다른 Slave ID(예: Heating Jacket 1/2/3이 같은 USART2를 공유)가 라운드로빈으로 같이
    /// 폴링된다. 이전엔 "다음에 도착한 프레임 딱 하나"만 보고 그게 다른 Slave ID나 CRC 오류 프레임이면
    /// 그 자리에서 바로 실패(Baseline/Fault/Recovery 판정 실패)로 끝내버렸다 — 라운드로빈 중 우연히
    /// 우리 차례가 아닌 프레임이 먼저 도착하면 검사가 무작위로 실패하는 원인이었다("가끔은 되고
    /// 가끔은 안 되는" 증상, 검사 시간도 비정상적으로 짧게 끝남). 실제 Modbus Slave 장치는 자기
    /// 앞으로 온 게 아니면 그냥 무시하고 계속 듣기만 하듯, 지정된 timeoutMs 예산 안에서 우리
    /// Slave ID로 온 요청이 실제로 나타날 때까지 다른 Slave ID/CRC 오류 프레임은 건너뛰고 계속
    /// 기다린다.</summary>
    private async Task<(byte[] Raw, ModbusSlaveRequest Request)?> ReceiveOwnRequestAsync(int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                Record(null, null, ModbusSlaveResponseResult.NoRequestReceived, $"{timeoutMs}ms 안에 이 Slave ID로 온 요청이 없음");
                return null;
            }

            var raw = await ReceiveRequestFrameAsync(remainingMs, ct).ConfigureAwait(false);
            if (raw is null)
            {
                Record(null, null, ModbusSlaveResponseResult.NoRequestReceived, $"{timeoutMs}ms 안에 요청이 전혀 오지 않음");
                return null;
            }

            var requestHex = ToHex(raw);
            var request = ModbusSlaveCodec.TryParse(raw);
            if (request is null)
            {
                // CRC 불일치 또는 구조 파싱 실패 — 표준 Modbus RTU 관례상 응답하지 않고, 같은 버스의
                // 다른 요청을 계속 기다린다(그 자리에서 실패로 끝내지 않음).
                Record(requestHex, null, ModbusSlaveResponseResult.CrcErrorIgnored, "CRC 또는 구조가 맞지 않아 응답하지 않음(표준 관례)");
                continue;
            }

            if (request.SlaveId != _store.SlaveId)
            {
                if (_busStores.TryGetValue(request.SlaveId, out var siblingStore))
                {
                    // 이 애플리케이션은 버스 전체의 Multi-Slave Emulator다. 형제 ID를 무시하면 IF_main이
                    // 그 ID에서 response timeout×retry를 모두 소비해 선택 대상의 다음 poll이 Gas는
                    // 1초, Heating은 3초 이상 늦어지고 Fault 단계가 시작되기 전에 ERROR가 난다.
                    await SendNormalResponseAsync(raw, request, siblingStore, ct).ConfigureAwait(false);
                    continue;
                }

                Record(requestHex, null, ModbusSlaveResponseResult.NotAddressedToUs,
                    $"요청 Slave ID {request.SlaveId}는 이 버스의 등록 대상이 아님 — 응답하지 않음");
                continue;
            }

            return (raw, request);
        }
    }


    public Task<ModbusSlaveExchange> ServiceNextBusRequestAsync(
        int timeoutMs,
        CancellationToken ct = default) =>
        ServiceNextBusRequestAsync(timeoutMs, null, ct);

    public async Task<ModbusSlaveExchange> ServiceNextBusRequestAsync(
        int timeoutMs,
        byte? suppressedSlaveId,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                Record(null, null, ModbusSlaveResponseResult.NoRequestReceived,
                    $"{timeoutMs}ms 안에 등록된 Slave 요청이 없음");
                throw new SerialPortTimeoutException(
                    _serial.CurrentOptions?.PortName ?? "ModbusSlave", timeoutMs);
            }

            var raw = await ReceiveRequestFrameAsync(remainingMs, ct).ConfigureAwait(false);
            if (raw is null)
                throw new SerialPortTimeoutException(
                    _serial.CurrentOptions?.PortName ?? "ModbusSlave", timeoutMs);

            var request = ModbusSlaveCodec.TryParse(raw);
            if (request is null)
            {
                Record(ToHex(raw), null, ModbusSlaveResponseResult.CrcErrorIgnored,
                    "CRC 또는 구조가 맞지 않아 응답하지 않음");
                continue;
            }

            if (!_busStores.TryGetValue(request.SlaveId, out var store))
            {
                Record(ToHex(raw), null, ModbusSlaveResponseResult.NotAddressedToUs,
                    $"요청 Slave ID {request.SlaveId}는 등록 대상이 아님");
                continue;
            }

            if (suppressedSlaveId.HasValue && request.SlaveId == suppressedSlaveId.Value)
            {
                return Record(ToHex(raw), null, ModbusSlaveResponseResult.FaultInjected,
                    $"FAULT 검사: Slave ID {request.SlaveId} 응답 차단");
            }

            return await SendNormalResponseAsync(raw, request, store, ct).ConfigureAwait(false);
        }
    }

    public async Task<ModbusSlaveExchange> ServiceNextRequestAsync(int timeoutMs, CancellationToken ct = default)
    {
        var own = await ReceiveOwnRequestAsync(timeoutMs, ct).ConfigureAwait(false);
        if (own is null)
            throw new SerialPortTimeoutException(_serial.CurrentOptions?.PortName ?? "ModbusSlave", timeoutMs);

        var (raw, request) = own.Value;
        var requestHex = ToHex(raw);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (response, result, detail) = BuildNormalResponse(request, _store);
        double? responseTimeMs = null;
        if (response is not null)
        {
            await _serial.SendAsync(response, 1000, ct).ConfigureAwait(false);
            responseTimeMs = sw.Elapsed.TotalMilliseconds;
        }

        return Record(requestHex, response is null ? null : ToHex(response), result, detail, responseTimeMs);
    }

    public async Task<ModbusSlaveExchange> ServiceNextRequestWithFaultAsync(ModbusSlaveFaultKind faultKind, int waitTimeoutMs, int delayMs, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(waitTimeoutMs);
        (byte[] Raw, ModbusSlaveRequest Request)? own;
        while (true)
        {
            var remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
                throw new SerialPortTimeoutException(_serial.CurrentOptions?.PortName ?? "ModbusSlave", waitTimeoutMs);

            own = await ReceiveOwnRequestAsync(remainingMs, ct).ConfigureAwait(false);
            if (own is null)
                throw new SerialPortTimeoutException(_serial.CurrentOptions?.PortName ?? "ModbusSlave", waitTimeoutMs);

            if (IsCompatibleRequest(faultKind, own.Value.Request)) break;

            // Byte Count/Data 변조는 읽기 응답에만 존재한다. 쓰기 요청에 필드 위치를 억지로 변조하면
            // 주소 byte를 Byte Count로 오인하는 전혀 다른 Fault가 되므로, 쓰기는 정상 응답하고 다음
            // 읽기 polling을 같은 timeout 예산 안에서 계속 기다린다.
            await SendNormalResponseAsync(own.Value.Raw, own.Value.Request, _store, ct).ConfigureAwait(false);
        }

        var (raw, request) = own.Value;
        var requestHex = ToHex(raw);

        var (validResponse, _, _) = BuildNormalResponse(request, _store);
        if (validResponse is null)
        {
            return Record(requestHex, null, ModbusSlaveResponseResult.ExceptionIllegalFunction,
                "정상 응답 자체를 만들 수 없는 요청(FC/주소 범위 밖)이라 Fault를 그 위에 적용할 수 없음");
        }

        // 요청을 다 받은 시점부터 측정한다 — "응답 지연" 케이스의 의도한 DelayMs도 그대로 포함시켜
        // 실제로 그 지연만큼 걸렸는지 로그에서 확인할 수 있게 한다.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (faultKind == ModbusSlaveFaultKind.DelayedResponse && delayMs > 0)
            await _delayAsync(delayMs, ct).ConfigureAwait(false);

        var framesToSend = ModbusSlaveResponseMutator.Mutate(validResponse, faultKind);
        foreach (var frame in framesToSend)
            await _serial.SendAsync(frame, 1000, ct).ConfigureAwait(false);
        double? responseTimeMs = framesToSend.Count == 0 ? null : sw.Elapsed.TotalMilliseconds;

        var responseHex = framesToSend.Count == 0 ? null : string.Join(" | ", framesToSend.Select(ToHex));
        return Record(requestHex, responseHex, ModbusSlaveResponseResult.FaultInjected,
            $"Fault({faultKind}) 응답 {framesToSend.Count}건 송신 완료 — 실제 컨트롤러 반응은 확인되지 않음", responseTimeMs);
    }

    private static bool IsCompatibleRequest(ModbusSlaveFaultKind faultKind, ModbusSlaveRequest request) =>
        faultKind is not (ModbusSlaveFaultKind.WrongByteCount or ModbusSlaveFaultKind.DataMismatch) ||
        request.FunctionCode is 0x01 or 0x02 or 0x03 or 0x04;

    /// <summary>표준 Modbus RTU 규칙대로 정상 응답 프레임을 만든다. 지원하지 않는 FC는 Illegal
    /// Function, 구성되지 않은 주소 범위는 Illegal Data Address Exception을 반환한다(추측이 아니라
    /// Modbus 표준 관례를 따른 것 — 실제 DIO/Heating/Gas 주변장치의 정확한 반응은 확인되지 않았다).</summary>
    private async Task<ModbusSlaveExchange> SendNormalResponseAsync(
        byte[] raw, ModbusSlaveRequest request, ModbusSlaveRegisterStore store, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (response, result, detail) = BuildNormalResponse(request, store);
        double? responseTimeMs = null;
        if (response is not null)
        {
            await _serial.SendAsync(response, 1000, ct).ConfigureAwait(false);
            responseTimeMs = sw.Elapsed.TotalMilliseconds;
        }

        return Record(ToHex(raw), response is null ? null : ToHex(response), result,
            $"동일 버스 Slave ID {store.SlaveId} 정상 대행 응답 — {detail}", responseTimeMs);
    }

    private (byte[]? Response, ModbusSlaveResponseResult Result, string Detail) BuildNormalResponse(
        ModbusSlaveRequest request, ModbusSlaveRegisterStore store)
    {
        switch (request.FunctionCode)
        {
            case 0x03:
            {
                var values = store.TryReadHolding(request.Address, request.Quantity);
                if (values is null)
                    return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x02), ModbusSlaveResponseResult.ExceptionIllegalAddress, UnregisteredDetail(request, "Holding Register", store));
                return (ModbusSlaveCodec.BuildReadRegistersResponse(request.SlaveId, request.FunctionCode, values), ModbusSlaveResponseResult.Success, "정상 응답(Holding Register 읽기)");
            }
            case 0x04:
            {
                var values = store.TryReadInput(request.Address, request.Quantity);
                if (values is null)
                    return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x02), ModbusSlaveResponseResult.ExceptionIllegalAddress, UnregisteredDetail(request, "Input Register", store));
                return (ModbusSlaveCodec.BuildReadRegistersResponse(request.SlaveId, request.FunctionCode, values), ModbusSlaveResponseResult.Success, "정상 응답(Input Register 읽기)");
            }
            case 0x01:
            {
                var bits = store.TryReadCoils(request.Address, request.Quantity);
                if (bits is null)
                    return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x02), ModbusSlaveResponseResult.ExceptionIllegalAddress, UnregisteredDetail(request, "Coil", store));
                return (ModbusSlaveCodec.BuildReadBitsResponse(request.SlaveId, request.FunctionCode, bits), ModbusSlaveResponseResult.Success, "정상 응답(Coil 읽기)");
            }
            case 0x02:
            {
                var bits = store.TryReadDiscreteInputs(request.Address, request.Quantity);
                if (bits is null)
                    return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x02), ModbusSlaveResponseResult.ExceptionIllegalAddress, UnregisteredDetail(request, "Discrete Input", store));
                return (ModbusSlaveCodec.BuildReadBitsResponse(request.SlaveId, request.FunctionCode, bits), ModbusSlaveResponseResult.Success, "정상 응답(Discrete Input 읽기)");
            }
            case 0x06:
            {
                var ok = store.TryWriteHolding(request.Address, request.WriteRegisterValues!);
                if (!ok)
                    return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x02), ModbusSlaveResponseResult.ExceptionIllegalAddress, UnregisteredDetail(request, "Holding Register 쓰기", store));
                return (ModbusSlaveCodec.BuildWriteEchoResponse(request.SlaveId, request.FunctionCode, request.Address, request.WriteRegisterValues![0]), ModbusSlaveResponseResult.Success, "정상 응답(단일 레지스터 쓰기 echo)");
            }
            case 0x10:
            {
                var ok = store.TryWriteHolding(request.Address, request.WriteRegisterValues!);
                if (!ok)
                    return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x02), ModbusSlaveResponseResult.ExceptionIllegalAddress, UnregisteredDetail(request, "Holding Register 쓰기", store));
                return (ModbusSlaveCodec.BuildWriteEchoResponse(request.SlaveId, request.FunctionCode, request.Address, request.Quantity), ModbusSlaveResponseResult.Success, "정상 응답(복수 레지스터 쓰기 echo)");
            }
            case 0x05:
            {
                var ok = store.TryWriteCoils(request.Address, request.WriteBitValues!);
                if (!ok)
                    return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x02), ModbusSlaveResponseResult.ExceptionIllegalAddress, UnregisteredDetail(request, "Coil 쓰기", store));
                var raw = (ushort)(request.WriteBitValues![0] ? 0xFF00 : 0x0000);
                return (ModbusSlaveCodec.BuildWriteEchoResponse(request.SlaveId, request.FunctionCode, request.Address, raw), ModbusSlaveResponseResult.Success, "정상 응답(단일 코일 쓰기 echo)");
            }
            case 0x0F:
            {
                var ok = store.TryWriteCoils(request.Address, request.WriteBitValues!);
                if (!ok)
                    return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x02), ModbusSlaveResponseResult.ExceptionIllegalAddress, UnregisteredDetail(request, "Coil 쓰기", store));
                return (ModbusSlaveCodec.BuildWriteEchoResponse(request.SlaveId, request.FunctionCode, request.Address, request.Quantity), ModbusSlaveResponseResult.Success, "정상 응답(복수 코일 쓰기 echo)");
            }
            default:
                return (ModbusSlaveCodec.BuildExceptionResponse(request.SlaveId, request.FunctionCode, 0x01), ModbusSlaveResponseResult.ExceptionIllegalFunction,
                    $"지원하지 않는 Function Code(FC {request.FunctionCode:X2}, Slave {request.SlaveId}) — 등록된 범위: {store.DescribeRegisteredRanges()}");
        }
    }

    /// <summary>REQUIREMENTS 4절 1~3번 — 미등록 주소로 첫 대상 ID 요청이 왔을 때 그냥 "구성되지 않은
    /// 주소 범위"라고만 말하지 않는다. 실제로 수신한 Slave ID/FC/시작 주소/Quantity와 현재 등록된
    /// Map 범위를 나란히 보여줘서, 이게 "검사 설정(프로파일) 오류"인지 판단할 근거를 남긴다 — 이
    /// 자체는 여전히 표준 Modbus Illegal Data Address 응답(<see cref="ModbusSlaveResponseResult.ExceptionIllegalAddress"/>)이다,
    /// 응답을 안 보내는 게 아니라 정직하게 예외를 보내되 로그 상세만 풍부하게 남긴다.</summary>
    private static string UnregisteredDetail(
        ModbusSlaveRequest request, string tableLabel, ModbusSlaveRegisterStore store) =>
        $"미등록 {tableLabel} 요청 — Slave {request.SlaveId}, FC {request.FunctionCode:X2}, 주소 {request.Address}, Quantity {request.Quantity}. " +
        $"등록된 범위: {store.DescribeRegisteredRanges()}.";

    /// <summary>완전한 요청 프레임 하나를 <see cref="_rxBuffer"/>에서 꺼내거나, timeoutMs 안에 프레임
    /// 하나를 채울 만큼도 못 받으면 null을 반환한다(예외를 던지지 않고 raw 그대로 반환 — 잘못된 CRC/
    /// 구조도 그대로 넘겨 파싱 단계에서 판단한다). 이전 호출에서 남겨둔 바이트(다음 프레임의 일부 또는
    /// 전부)가 <see cref="_rxBuffer"/>에 이미 있으면 새로 Receive하지 않고 그것부터 먼저 쓴다.</summary>
    private async Task<byte[]?> ReceiveRequestFrameAsync(int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        int? expectedLength = ModbusSlaveCodec.TryComputeExpectedRequestLength(_rxBuffer);

        while (!(expectedLength.HasValue && _rxBuffer.Count >= expectedLength.Value))
        {
            var remainingMs = (deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                if (_rxBuffer.Count == 0) return null;
                break;
            }

            var sliceMs = (int)Math.Max(1, Math.Min(_rxBuffer.Count == 0 ? remainingMs : FrameGapMs, remainingMs));
            byte[] chunk;
            try
            {
                chunk = await _serial.ReceiveAsync(MaxRawFrameLength, sliceMs, ct).ConfigureAwait(false);
            }
            catch (SerialPortTimeoutException)
            {
                if (_rxBuffer.Count == 0) continue;
                break;
            }

            _rxBuffer.AddRange(chunk);
            if (expectedLength is null)
                expectedLength = ModbusSlaveCodec.TryComputeExpectedRequestLength(_rxBuffer);
        }

        if (_rxBuffer.Count == 0) return null;

        // 라운드로빈 버스에서 우리가 다른 처리(예: 응답 지연 Fault의 Task.Delay)로 잠시 못 읽는 동안
        // 다른 Slave ID로 가는 요청까지, 심지어 그 뒤에 우리 앞으로 온 또 다른 요청까지 같은 저수준
        // 수신 청크에 함께 실려올 수 있다 — 이전엔 한 프레임 길이(expectedLength)를 넘는 나머지를
        // 통째로 버려서, 뒤에 붙어온 프레임(우리 앞으로 온 정상 요청이었을 수도 있음)까지 함께
        // 사라졌다. 이제 딱 한 프레임만 꺼내고, 남는 바이트는 버리지 않고 _rxBuffer에 그대로 둬서
        // 다음 ReceiveRequestFrameAsync 호출이 이어서 쓴다.
        if (expectedLength is { } len && _rxBuffer.Count > len)
        {
            var frame = _rxBuffer.GetRange(0, len).ToArray();
            _rxBuffer.RemoveRange(0, len);
            return frame;
        }

        var all = _rxBuffer.ToArray();
        _rxBuffer.Clear();
        return all;
    }

    private ModbusSlaveExchange Record(string? requestHex, string? responseHex, ModbusSlaveResponseResult result, string detail, double? responseTimeMs = null)
    {
        var exchange = new ModbusSlaveExchange(DateTime.Now, requestHex, responseHex, result, detail, responseTimeMs);
        lock (_gate)
        {
            _exchanges.Add(exchange);
            if (_exchanges.Count > MaxExchangeHistory)
                _exchanges.RemoveRange(0, _exchanges.Count - MaxExchangeHistory);
        }
        ExchangeRecorded?.Invoke(this, exchange);
        return exchange;
    }

    private static string ToHex(byte[] data) => string.Join(" ", data.Select(b => b.ToString("X2")));
}
