using System.Diagnostics;
using OperateTest.App.Services.Serial;

namespace OperateTest.App.Services.Modbus;

/// <summary>
/// <see cref="IModbusMasterService"/>의 실제 구현. <see cref="ISerialPortService"/> 하나를 감싸
/// Modbus RTU Master 요청-응답 왕복을 수행한다. 이 클래스는 프레임 생성/CRC/파싱을
/// <see cref="ModbusRtuCodec"/>(순수 함수)에 위임하고, 스스로는 I/O 순서 제어와 Transaction
/// 기록만 담당한다. 요청 송신 시작부터 응답(또는 timeout 확정)까지의 실측 시간을
/// <see cref="ModbusTransaction.ResponseTimeMs"/>에 채운다(OPEN_ITEMS_AUDIT.md OI-24).
/// </summary>
public sealed class ModbusMasterService : IModbusMasterService
{
    /// <summary>첫 바이트 이후, 프레임이 끝났다고 볼 수 있는 무응답 구간(ms). 표준 Modbus RTU의
    /// "3.5문자 시간" 대신, 이 프로젝트의 Serial 계층이 바이트를 청크 단위로 넘겨주는 실제 동작에
    /// 맞춰 넉넉한 고정값을 쓴다.</summary>
    private const int FrameGapMs = 20;

    private const int MaxFrameLength = 256;

    private readonly ISerialPortService _serial;
    private readonly SemaphoreSlim _transactionLock = new(1, 1);
    private readonly List<ModbusTransaction> _transactions = new();
    private readonly object _transactionsGate = new();

    public ModbusMasterService(ISerialPortService serial)
    {
        _serial = serial;
    }

    public IReadOnlyList<ModbusTransaction> Transactions
    {
        get
        {
            lock (_transactionsGate)
            {
                return _transactions.ToList();
            }
        }
    }

    public event EventHandler<ModbusTransaction>? TransactionRecorded;

    public void ClearTransactions()
    {
        lock (_transactionsGate)
        {
            _transactions.Clear();
        }
    }

    public async Task<ModbusReadResult> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct = default)
        => await ReadRegistersAsync(ModbusFunctionCode.ReadHoldingRegisters, slaveId, startAddress, quantity, timeoutMs, ct).ConfigureAwait(false);

    public async Task<ModbusReadResult> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct = default)
        => await ReadRegistersAsync(ModbusFunctionCode.ReadInputRegisters, slaveId, startAddress, quantity, timeoutMs, ct).ConfigureAwait(false);

    private async Task<ModbusReadResult> ReadRegistersAsync(byte functionCode, byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct)
    {
        var request = ModbusRtuCodec.BuildReadRequest(slaveId, functionCode, startAddress, quantity);
        var requestHex = ToHex(request);

        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (response, elapsedMs) = await SendAndReceiveAsync(slaveId, functionCode, request, requestHex, isWriteRequest: false, timeoutMs, ct).ConfigureAwait(false);
            var responseHex = ToHex(response);

            try
            {
                var result = ModbusRtuCodec.ParseReadResponse(slaveId, functionCode, startAddress, quantity, response);
                Record(slaveId, functionCode, requestHex, responseHex, ModbusTransactionResult.Success, "정상 응답", elapsedMs);
                return result;
            }
            catch (ModbusExceptionResponseException ex)
            {
                Record(slaveId, functionCode, requestHex, responseHex, ModbusTransactionResult.Exception, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusCrcMismatchException ex)
            {
                Record(slaveId, functionCode, requestHex, responseHex, ModbusTransactionResult.CrcError, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusProtocolException ex)
            {
                Record(slaveId, functionCode, requestHex, responseHex, ModbusTransactionResult.Mismatch, ex.Message, elapsedMs);
                throw;
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    public async Task<ModbusBitReadResult> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct = default)
        => await ReadBitsAsync(ModbusFunctionCode.ReadCoils, slaveId, startAddress, quantity, timeoutMs, ct).ConfigureAwait(false);

    public async Task<ModbusBitReadResult> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct = default)
        => await ReadBitsAsync(ModbusFunctionCode.ReadDiscreteInputs, slaveId, startAddress, quantity, timeoutMs, ct).ConfigureAwait(false);

    private async Task<ModbusBitReadResult> ReadBitsAsync(byte functionCode, byte slaveId, ushort startAddress, ushort quantity, int timeoutMs, CancellationToken ct)
    {
        var request = ModbusRtuCodec.BuildReadBitsRequest(slaveId, functionCode, startAddress, quantity);
        var requestHex = ToHex(request);

        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (response, elapsedMs) = await SendAndReceiveAsync(slaveId, functionCode, request, requestHex, isWriteRequest: false, timeoutMs, ct).ConfigureAwait(false);
            var responseHex = ToHex(response);

            try
            {
                var result = ModbusRtuCodec.ParseReadBitsResponse(slaveId, functionCode, startAddress, quantity, response);
                Record(slaveId, functionCode, requestHex, responseHex, ModbusTransactionResult.Success, "정상 응답", elapsedMs);
                return result;
            }
            catch (ModbusExceptionResponseException ex)
            {
                Record(slaveId, functionCode, requestHex, responseHex, ModbusTransactionResult.Exception, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusCrcMismatchException ex)
            {
                Record(slaveId, functionCode, requestHex, responseHex, ModbusTransactionResult.CrcError, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusProtocolException ex)
            {
                Record(slaveId, functionCode, requestHex, responseHex, ModbusTransactionResult.Mismatch, ex.Message, elapsedMs);
                throw;
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    public async Task<ModbusWriteResult> WriteSingleCoilAsync(byte slaveId, ushort address, bool value, int timeoutMs, CancellationToken ct = default)
    {
        const byte fc = ModbusFunctionCode.WriteSingleCoil;
        var request = ModbusRtuCodec.BuildWriteSingleCoilRequest(slaveId, address, value);
        var requestHex = ToHex(request);

        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (response, elapsedMs) = await SendAndReceiveAsync(slaveId, fc, request, requestHex, isWriteRequest: true, timeoutMs, ct).ConfigureAwait(false);
            var responseHex = ToHex(response);

            try
            {
                var result = ModbusRtuCodec.ParseWriteSingleCoilResponse(slaveId, address, value, response);
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Success, "정상 응답(Write Echo 일치)", elapsedMs);
                return result;
            }
            catch (ModbusExceptionResponseException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Exception, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusCrcMismatchException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.CrcError, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusProtocolException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Mismatch, ex.Message, elapsedMs);
                throw;
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    public async Task<ModbusWriteResult> WriteMultipleCoilsAsync(byte slaveId, ushort startAddress, bool[] values, int timeoutMs, CancellationToken ct = default)
    {
        const byte fc = ModbusFunctionCode.WriteMultipleCoils;
        var request = ModbusRtuCodec.BuildWriteMultipleCoilsRequest(slaveId, startAddress, values);
        var requestHex = ToHex(request);

        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (response, elapsedMs) = await SendAndReceiveAsync(slaveId, fc, request, requestHex, isWriteRequest: true, timeoutMs, ct).ConfigureAwait(false);
            var responseHex = ToHex(response);

            try
            {
                var result = ModbusRtuCodec.ParseWriteMultipleCoilsResponse(slaveId, startAddress, values.Length, response);
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Success, "정상 응답(Write Echo 일치)", elapsedMs);
                return result;
            }
            catch (ModbusExceptionResponseException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Exception, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusCrcMismatchException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.CrcError, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusProtocolException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Mismatch, ex.Message, elapsedMs);
                throw;
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    public async Task<ModbusWriteResult> WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, int timeoutMs, CancellationToken ct = default)
    {
        const byte fc = ModbusFunctionCode.WriteSingleRegister;
        var request = ModbusRtuCodec.BuildWriteSingleRegisterRequest(slaveId, address, value);
        var requestHex = ToHex(request);

        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (response, elapsedMs) = await SendAndReceiveAsync(slaveId, fc, request, requestHex, isWriteRequest: true, timeoutMs, ct).ConfigureAwait(false);
            var responseHex = ToHex(response);

            try
            {
                var result = ModbusRtuCodec.ParseWriteSingleRegisterResponse(slaveId, address, value, response);
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Success, "정상 응답(Write Echo 일치)", elapsedMs);
                return result;
            }
            catch (ModbusExceptionResponseException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Exception, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusCrcMismatchException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.CrcError, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusProtocolException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Mismatch, ex.Message, elapsedMs);
                throw;
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    public async Task<ModbusWriteResult> WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, int timeoutMs, CancellationToken ct = default)
    {
        const byte fc = ModbusFunctionCode.WriteMultipleRegisters;
        var request = ModbusRtuCodec.BuildWriteMultipleRegistersRequest(slaveId, startAddress, values);
        var requestHex = ToHex(request);

        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (response, elapsedMs) = await SendAndReceiveAsync(slaveId, fc, request, requestHex, isWriteRequest: true, timeoutMs, ct).ConfigureAwait(false);
            var responseHex = ToHex(response);

            try
            {
                var result = ModbusRtuCodec.ParseWriteMultipleRegistersResponse(slaveId, startAddress, values.Length, response);
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Success, "정상 응답(Write Echo 일치)", elapsedMs);
                return result;
            }
            catch (ModbusExceptionResponseException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Exception, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusCrcMismatchException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.CrcError, ex.Message, elapsedMs);
                throw;
            }
            catch (ModbusProtocolException ex)
            {
                Record(slaveId, fc, requestHex, responseHex, ModbusTransactionResult.Mismatch, ex.Message, elapsedMs);
                throw;
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>요청을 보내고 완전한 응답 프레임(또는 시간 안에 다 못 받은 부분 프레임)을 반환한다.
    /// 반환하는 경과 시간(ms)은 이 메서드 진입(요청 송신 시작 직전)부터 응답 확정까지를 잰 것이며,
    /// 호출자가 그대로 <see cref="ModbusTransaction.ResponseTimeMs"/>에 채운다. 한 바이트도 받지
    /// 못한 채 시간이 끝나면 Timeout으로 기록하고 <see cref="ModbusTimeoutException"/>을 던진다.</summary>
    private async Task<(byte[] Bytes, double ElapsedMs)> SendAndReceiveAsync(byte slaveId, byte functionCode, byte[] request, string requestHex, bool isWriteRequest, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _serial.SendAsync(request, timeoutMs, ct).ConfigureAwait(false);
        }
        catch (SerialPortTimeoutException ex)
        {
            sw.Stop();
            Record(slaveId, functionCode, requestHex, null, ModbusTransactionResult.Timeout, "요청 송신 자체가 timeout: " + ex.Message, sw.Elapsed.TotalMilliseconds);
            throw new ModbusTimeoutException(slaveId, functionCode, timeoutMs, ex);
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var buffer = new List<byte>();
        int? expectedLength = null;

        while (true)
        {
            if (expectedLength.HasValue && buffer.Count >= expectedLength.Value)
                break;

            var remainingMs = (deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                if (buffer.Count == 0)
                {
                    sw.Stop();
                    Record(slaveId, functionCode, requestHex, null, ModbusTransactionResult.Timeout, $"{timeoutMs}ms 안에 응답이 전혀 없음(무응답/Timeout)", sw.Elapsed.TotalMilliseconds);
                    throw new ModbusTimeoutException(slaveId, functionCode, timeoutMs);
                }
                break; // 일부만 수신된 채 시간 종료 - 이후 파싱 단계에서 길이/CRC 불일치로 드러난다.
            }

            var sliceMs = (int)Math.Max(1, Math.Min(buffer.Count == 0 ? remainingMs : FrameGapMs, remainingMs));

            byte[] chunk;
            try
            {
                chunk = await _serial.ReceiveAsync(MaxFrameLength, sliceMs, ct).ConfigureAwait(false);
            }
            catch (SerialPortTimeoutException)
            {
                if (buffer.Count == 0) continue; // 아직 전체 timeout 안 끝났으면 계속 대기
                break; // 프레임 사이 조용한 구간으로 보고 여기서 수신을 마친다.
            }

            buffer.AddRange(chunk);

            if (expectedLength is null)
                expectedLength = ModbusRtuCodec.TryComputeExpectedLength(functionCode, isWriteRequest, buffer);
        }

        sw.Stop();
        var bytes = expectedLength.HasValue && buffer.Count > expectedLength.Value
            ? buffer.Take(expectedLength.Value).ToArray()
            : buffer.ToArray();
        return (bytes, sw.Elapsed.TotalMilliseconds);
    }

    private void Record(byte slaveId, byte functionCode, string requestHex, string? responseHex, ModbusTransactionResult result, string detail, double? responseTimeMs = null)
    {
        var tx = new ModbusTransaction(DateTime.Now, slaveId, functionCode, requestHex, responseHex, result, detail, responseTimeMs);
        lock (_transactionsGate)
        {
            _transactions.Add(tx);
        }
        TransactionRecorded?.Invoke(this, tx);
    }

    private static string ToHex(byte[] data) => string.Join(" ", data.Select(b => b.ToString("X2")));
}
