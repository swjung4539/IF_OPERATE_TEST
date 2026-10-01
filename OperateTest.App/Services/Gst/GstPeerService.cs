using OperateTest.App.Services.Serial;

namespace OperateTest.App.Services.Gst;

/// <summary><see cref="IGstPeerService"/>의 실제 구현. <see cref="ISerialPortService"/> 하나만 직접
/// 잡고 raw 송수신하며, 프레임 생성/변형/파싱은 <see cref="GstFrameCodec"/>/<see cref="GstFrameMutator"/>
/// (둘 다 순수 함수)에 위임한다.</summary>
public sealed class GstPeerService : IGstPeerService
{
    private const int ReassemblyTimeoutMs = 200; // GST_RX_REASSEMBLY_TIMEOUT_MS와 동일 값
    private const int PollSliceMs = 20;
    private const int MaxTransactionHistory = 20_000;

    private readonly ISerialPortService _serial;
    private readonly Func<int, CancellationToken, Task> _delayAsync;
    private readonly List<byte> _reassemblyBuf = new();
    private readonly SemaphoreSlim _receiveGate = new(1, 1);
    private DateTime? _reassemblyStartedAt;
    private readonly List<GstTransaction> _transactions = new();
    private readonly object _gate = new();

    public GstPeerService(
        ISerialPortService serial,
        Func<int, CancellationToken, Task>? delayAsync = null)
    {
        _serial = serial;
        _delayAsync = delayAsync ?? ((delayMs, ct) => Task.Delay(delayMs, ct));
    }

    public IReadOnlyList<GstTransaction> Transactions
    {
        get { lock (_gate) { return _transactions.ToList(); } }
    }

    public event EventHandler<GstTransaction>? TransactionRecorded;

    public void ClearTransactions()
    {
        lock (_gate) { _transactions.Clear(); }
    }

    public async Task ResetReceiveStateAsync(CancellationToken ct = default)
    {
        await _receiveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _reassemblyBuf.Clear();
            _reassemblyStartedAt = null;
        }
        finally
        {
            _receiveGate.Release();
        }
    }

    public async Task<GstFrame> ReceiveFromControllerAsync(int timeoutMs, CancellationToken ct = default)
    {
        await _receiveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReceiveCoreAsync(timeoutMs, ct).ConfigureAwait(false);
        }
        finally
        {
            _receiveGate.Release();
        }
    }

    private async Task<GstFrame> ReceiveCoreAsync(int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // 재조립 timeout: 이전에 쌓아둔 조각이 있는데 너무 오래 이어지지 않으면 버린다
            // (`GstProtocol_RxStep`의 reassembly_start_ms 로직과 동일).
            if (_reassemblyBuf.Count > 0 && _reassemblyStartedAt is { } startedAt &&
                (DateTime.UtcNow - startedAt).TotalMilliseconds >= ReassemblyTimeoutMs)
            {
                _reassemblyBuf.Clear();
                _reassemblyStartedAt = null;
            }

            // 직전 read에 여러 프레임이 함께 들어왔을 수 있으므로, 새로 읽기 전에
            // 버퍼에 이미 완성된 프레임이 있는지 먼저 확인한다.
            if (TryTakeCompleteFrame(out var bufferedFrame))
                return ParseAndRecord(bufferedFrame);

            var remainingMs = (deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                Record(GstFrameDirection.Rx, null, GstTransactionResult.NoResponse, $"{timeoutMs}ms 안에 완전한 프레임 없음(무응답/Timeout)");
                throw new SerialPortTimeoutException(_serial.CurrentOptions?.PortName ?? "GST", timeoutMs);
            }

            // 아직 아무 것도 못 받은 상태면 남은 시간 전체를 한 번에 기다리고, 이미 조각을 받고
            // 다음 조각을 기다리는 중이면 짧은 슬라이스로 폴링한다(ModbusMasterService.SendAndReceiveAsync와
            // 같은 원칙 — 실제 시간만큼 대기하도록 해 매 호출이 정확히 한 번의 "기다림"을 대표한다).
            var sliceMs = (int)Math.Max(1, Math.Min(_reassemblyBuf.Count == 0 ? remainingMs : PollSliceMs, remainingMs));
            byte[] chunk;
            try
            {
                chunk = await _serial.ReceiveAsync(GstFrameCodec.MaxFrameLength, sliceMs, ct).ConfigureAwait(false);
            }
            catch (SerialPortTimeoutException)
            {
                continue;
            }

            if (_reassemblyBuf.Count == 0)
                _reassemblyStartedAt = DateTime.UtcNow;

            _reassemblyBuf.AddRange(chunk);
        }
    }

    private bool TryTakeCompleteFrame(out byte[] complete)
    {
        complete = Array.Empty<byte>();

        while (true)
        {
            SynchronizeToHeader();
            if (_reassemblyBuf.Count < 7) return false;

            var expectedLen = GstFrameCodec.TryComputeExpectedLength(_reassemblyBuf)!.Value;

            // 앞 프레임이 덜 들어온 상태에서 새 GST Header가 들어오면 앞 프레임은 폐기한다.
            // 그렇지 않으면 새 프레임 시작부를 앞 프레임의 Data로 삼켜 Tail/CRC 오류로 오판한다.
            var nestedHeader = FindHeaderIndex(1);
            if (nestedHeader > 0 && nestedHeader < expectedLen)
            {
                Record(GstFrameDirection.Rx, _reassemblyBuf.Take(nestedHeader).ToArray(),
                    GstTransactionResult.PartialFrame, "불완전 GST 프레임 뒤 새 Header 수신 - 새 프레임부터 재동기화");
                _reassemblyBuf.RemoveRange(0, nestedHeader);
                _reassemblyStartedAt = DateTime.UtcNow;
                continue;
            }

            if (expectedLen > GstFrameCodec.MaxFrameLength)
            {
                Record(GstFrameDirection.Rx, _reassemblyBuf.Take(7).ToArray(),
                    GstTransactionResult.LengthMismatch,
                    $"GST Length 필드 {_reassemblyBuf[6]}가 최대 {GstFrameCodec.MaxDataLength} 초과");
                _reassemblyBuf.RemoveAt(0);
                continue;
            }

            if (_reassemblyBuf.Count < expectedLen) return false;

            complete = _reassemblyBuf.Take(expectedLen).ToArray();
            _reassemblyBuf.RemoveRange(0, expectedLen);
            _reassemblyStartedAt = _reassemblyBuf.Count > 0 ? DateTime.UtcNow : null;
            return true;
        }
    }

    private void SynchronizeToHeader()
    {
        var headerIndex = FindHeaderIndex(0);
        if (headerIndex >= 0)
        {
            if (headerIndex > 0) _reassemblyBuf.RemoveRange(0, headerIndex);
            return;
        }

        // 다음 read에서 "GST"가 완성될 수 있는 끝부분만 남긴다.
        var keep = _reassemblyBuf.Count >= 2 &&
                   _reassemblyBuf[^2] == GstFrameCodec.Header0 &&
                   _reassemblyBuf[^1] == GstFrameCodec.Header1
            ? 2
            : _reassemblyBuf.Count >= 1 && _reassemblyBuf[^1] == GstFrameCodec.Header0 ? 1 : 0;
        if (_reassemblyBuf.Count > keep)
            _reassemblyBuf.RemoveRange(0, _reassemblyBuf.Count - keep);
    }

    private int FindHeaderIndex(int startIndex)
    {
        for (var i = startIndex; i <= _reassemblyBuf.Count - 3; i++)
        {
            if (_reassemblyBuf[i] == GstFrameCodec.Header0 &&
                _reassemblyBuf[i + 1] == GstFrameCodec.Header1 &&
                _reassemblyBuf[i + 2] == GstFrameCodec.Header2)
                return i;
        }
        return -1;
    }

    private GstFrame ParseAndRecord(byte[] complete)
    {
        try
        {
            var frame = GstFrameCodec.Parse(complete);
            Record(GstFrameDirection.Rx, complete, GstTransactionResult.Success,
                $"fc=0x{frame.FunctionCode:X2} addr={frame.Address} qty={frame.Registers.Length}");
            return frame;
        }
        catch (GstProtocolException ex)
        {
            Record(GstFrameDirection.Rx, complete, Classify(ex), ex.Message);
            throw;
        }
    }

    public Task SendStatusAsync(ushort[] registers, CancellationToken ct = default)
    {
        var frame = GstFrameCodec.Build(GstFunctionCode.Read, GstProtocolAddresses.ReadStart, registers);
        return SendRawAsync(frame, ct);
    }

    public Task SendFaultFrameAsync(ushort[] registers, GstFaultKind kind, CancellationToken ct = default)
    {
        var frame = GstFrameMutator.Mutate(GstFunctionCode.Read, GstProtocolAddresses.ReadStart, registers, kind);
        return SendRawAsync(frame, ct);
    }

    public async Task SendDelayedAsync(ushort[] registers, int delayMs, CancellationToken ct = default)
    {
        if (delayMs > 0)
            await _delayAsync(delayMs, ct).ConfigureAwait(false);
        await SendStatusAsync(registers, ct).ConfigureAwait(false);
    }

    private async Task SendRawAsync(byte[] frame, CancellationToken ct)
    {
        await _serial.SendAsync(frame, 1000, ct).ConfigureAwait(false);
        Record(GstFrameDirection.Tx, frame, GstTransactionResult.Success, "송신 완료(내용 유효성은 이 계층에서 판단하지 않음)");
    }

    private void Record(GstFrameDirection direction, byte[]? raw, GstTransactionResult result, string detail)
    {
        var tx = new GstTransaction(DateTime.Now, direction, raw is null ? "" : ToHex(raw), result, detail);
        lock (_gate)
        {
            _transactions.Add(tx);
            if (_transactions.Count > MaxTransactionHistory)
                _transactions.RemoveRange(0, _transactions.Count - MaxTransactionHistory);
        }
        TransactionRecorded?.Invoke(this, tx);
    }

    private static GstTransactionResult Classify(GstProtocolException ex) => ex switch
    {
        GstHeaderMismatchException => GstTransactionResult.HeaderInvalid,
        GstTailMismatchException => GstTransactionResult.TailInvalid,
        GstCrcMismatchException => GstTransactionResult.CrcError,
        GstFunctionCodeInvalidException => GstTransactionResult.FunctionCodeInvalid,
        GstLengthMismatchException => GstTransactionResult.LengthMismatch,
        GstIncompleteFrameException => GstTransactionResult.PartialFrame,
        _ => GstTransactionResult.PartialFrame
    };

    private static string ToHex(byte[] data) => string.Join(" ", data.Select(b => b.ToString("X2")));
}

/// <summary>GST 자체 주소 공간 상수(COMMUNICATION_SPEC.md 11절/`a_gst_protocol.c:20-25`).</summary>
public static class GstProtocolAddresses
{
    public const ushort WriteStart = 201, WriteEnd = 241; // 컨트롤러→peer 프레임에 담기는 global HR 범위(app→GST)
    public const ushort ReadStart = 242, ReadEnd = 282;   // peer→컨트롤러 프레임이 반영되는 global HR 범위(GST→app)
    public const ushort ReservedStart = 283, ReservedEnd = 322; // 로컬 커밋만, GST 전선 트래픽 없음
}
