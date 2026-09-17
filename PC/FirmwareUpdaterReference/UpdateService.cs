using System.IO.Ports;

namespace FirmwareUpdater;

/// <summary>
/// 실제 업데이트 시퀀스(트리거 -> 재연결 -> PING -> ERASE -> WRITE_CHUNK 반복
/// -> FINALIZE -> REBOOT)를 담당하는 클래스. UI(MainForm)는 이 클래스를 호출만
/// 하고, 여기서 로그/진행률을 이벤트로 올려준다.
/// </summary>
public sealed class UpdateService
{
    // 부트로더가 WRITE_CHUNK 요청이 USB 패킷 여러 개(각 64바이트)로 나뉘어
    // 도착하면 응답을 못 하는 버그가 있음을 실측으로 확인했다(원인 미해결).
    // 프레임 전체(offset 4B + data + CRC 등 오버헤드 11B)가 USB Full-Speed
    // 패킷 하나(64바이트) 안에 항상 들어가도록 데이터 부분을 48바이트로 제한해
    // 우회한다: 5(header)+4(offset)+48(data)+2(crc) = 59바이트 <= 64.
    private const int ChunkSize = 48;

    /// <summary>사람이 읽을 진행 로그 한 줄.</summary>
    public event Action<string>? LogMessage;

    /// <summary>0~100 사이의 전체 진행률.</summary>
    public event Action<int>? ProgressChanged;

    /// <summary>
    /// 실제 쓰기 없이 PING만 보내서 장치 상태(활성 슬롯, 부팅 가능 여부,
    /// PENDING_TEST/재시도 카운터 등)를 로그로 확인만 하고 싶을 때 사용한다.
    /// "연결 확인" 버튼에서 호출된다.
    /// </summary>
    public async Task CheckStatusAsync(string portName, CancellationToken ct)
    {
        Log($"포트 {portName} 연결 시도...");
        BootProtocol.Frame ping = await ConnectAndPingAsync(portName, ct);
        LogPingDiagnostics(ping);
    }

    /// <summary>
    /// 진단용: 부트로더에 이미 상주 중인 상태에서, 지정한 슬롯 주소의 Flash
    /// 내용을 READ_FLASH로 48바이트씩 읽어 로컬 파일(expected)과 바이트 단위로
    /// 직접 비교한다. 디버거의 Memory 뷰가 신뢰하기 어려운 상황에서, USB
    /// 프로토콜 자체로 "실제로 Flash에 뭐가 들어있는지"를 확실하게 확인하기
    /// 위한 기능이다. 다른 곳이 아니라 여기서 불일치가 나온다면 WRITE_CHUNK
    /// 단계에서 진짜로 데이터가 잘못 기록됐다는 확정적 증거가 된다.
    /// </summary>
    public async Task VerifyFlashAsync(string portName, uint baseAddress, byte[] expected, CancellationToken ct)
    {
        const int readChunk = 48;

        Log($"포트 {portName} 연결 시도...");
        BootProtocol.Frame ping = await ConnectAndPingAsync(portName, ct);
        LogPingDiagnostics(ping);

        using var port = OpenPort(portName);

        int mismatchCount = 0;
        int firstMismatchOffset = -1;

        for (int offset = 0; offset < expected.Length; offset += readChunk)
        {
            ct.ThrowIfCancellationRequested();

            int len = Math.Min(readChunk, expected.Length - offset);
            var payload = new byte[6];
            BitConverter.GetBytes((uint)(baseAddress + offset)).CopyTo(payload, 0);
            BitConverter.GetBytes((ushort)len).CopyTo(payload, 4);

            var frame = BootProtocol.BuildFrame(BootProtocol.Cmd.ReadFlash, payload);
            port.Write(frame, 0, frame.Length);
            var resp = BootProtocol.ReadFrame(port.BaseStream, 1000);

            if (resp == null || !resp.IsOk)
            {
                Log($"오프셋 {offset}: READ_FLASH 실패(응답 없음 또는 FAIL) - 검증 중단");
                return;
            }

            byte[] actual = resp.Payload[1..(1 + len)];
            for (int i = 0; i < len; i++)
            {
                if (actual[i] != expected[offset + i])
                {
                    if (firstMismatchOffset < 0)
                    {
                        firstMismatchOffset = offset + i;
                    }
                    mismatchCount++;
                }
            }

            ProgressChanged?.Invoke((int)((offset + len) * 100L / expected.Length));
        }

        if (mismatchCount == 0)
        {
            Log($"검증 완료: {expected.Length:N0} bytes 전부 일치. Flash 내용은 원본 파일과 정확히 같습니다.");
        }
        else
        {
            Log($"검증 완료: 불일치 {mismatchCount}바이트 발견. 첫 불일치 오프셋 = {firstMismatchOffset} " +
                $"(주소 0x{baseAddress + firstMismatchOffset:X8})");
        }
    }

    public async Task RunAsync(string portName, byte[] slotAFirmware, byte[] slotBFirmware,
        (byte Major, byte Minor, byte Patch) version, CancellationToken ct)
    {
        Log($"포트 {portName} 연결 시도...");

        BootProtocol.Frame ping = await ConnectAndPingAsync(portName, ct);
        LogPingDiagnostics(ping);

        byte[] p = ping.Payload;
        var activeSlot = (BootProtocol.SlotId)p[4];
        var targetSlot = activeSlot == BootProtocol.SlotId.SlotA
            ? BootProtocol.SlotId.SlotB
            : BootProtocol.SlotId.SlotA;

        Log($"현재 실행 중인 슬롯: {activeSlot}. 새 펌웨어는 {targetSlot}에 기록합니다.");

        byte[] targetFirmware = targetSlot == BootProtocol.SlotId.SlotA ? slotAFirmware : slotBFirmware;

        using var port = OpenPort(portName);

        // 2단계: 대상 슬롯 소거. 크기가 클수록(최대 224KB) 페이지 소거 시간이
        // 늘어나므로(페이지당 최대 약 24ms) 응답 대기 시간을 넉넉히 잡는다.
        Log($"슬롯 소거 중... ({targetFirmware.Length:N0} bytes)");
        var erasePayload = new byte[5];
        erasePayload[0] = (byte)targetSlot;
        BitConverter.GetBytes((uint)targetFirmware.Length).CopyTo(erasePayload, 1);
        SendAndExpectOk(port, BootProtocol.Cmd.EraseSlot, erasePayload, timeoutMs: 6000,
            "ERASE_SLOT 실패 - 장치가 소거 명령을 거부했습니다.");

        // 3단계: 256바이트 청크로 나눠 순차 기록. 실패한 청크는 몇 차례
        // 재시도하고, 그래도 안 되면 전체 업데이트를 중단한다(대상 슬롯이
        // 아직 활성화(FINALIZE)되지 않았으므로 기존 펌웨어는 안전하다).
        int total = targetFirmware.Length;
        int offset = 0;
        while (offset < total)
        {
            ct.ThrowIfCancellationRequested();

            int len = Math.Min(ChunkSize, total - offset);
            var chunkPayload = new byte[4 + len];
            BitConverter.GetBytes((uint)offset).CopyTo(chunkPayload, 0);
            Array.Copy(targetFirmware, offset, chunkPayload, 4, len);

            const int maxRetry = 3;
            bool ok = false;
            for (int attempt = 1; attempt <= maxRetry && !ok; attempt++)
            {
                var frame = BootProtocol.BuildFrame(BootProtocol.Cmd.WriteChunk, chunkPayload);
                port.Write(frame, 0, frame.Length);
                var resp = BootProtocol.ReadFrame(port.BaseStream, 1000);
                ok = resp != null && resp.IsOk;

                if (!ok)
                {
                    // 타임아웃(응답 자체가 없음)인지, 장치가 명시적으로 FAIL을
                    // 돌려준 것인지 구분해서 로그로 남긴다 - 원인 파악에 필요.
                    string detail = resp == null
                        ? "응답 없음(타임아웃)"
                        : $"FAIL 응답 수신(payload={BitConverter.ToString(resp.Payload)})";
                    Log($"오프셋 {offset} (len={len}) 청크 전송 실패({detail}), 재시도 {attempt}/{maxRetry}");
                }
            }

            if (!ok)
            {
                throw new InvalidOperationException(
                    $"오프셋 {offset}에서 WRITE_CHUNK가 {maxRetry}회 모두 실패했습니다. 업데이트를 중단합니다.");
            }

            offset += len;
            ProgressChanged?.Invoke((int)(offset * 100L / total));
        }

        // 4단계: 플래시에 실제로 기록된 내용을 리드백해서 계산한 CRC32와,
        // 우리가 보낸 파일의 CRC32가 일치하는지 장치 스스로 검증하게 한다.
        Log("검증(FINALIZE) 중...");
        uint crc32 = BootProtocol.Crc32(targetFirmware, 0, targetFirmware.Length);
        var finalizePayload = new byte[11];
        BitConverter.GetBytes((uint)total).CopyTo(finalizePayload, 0);
        BitConverter.GetBytes(crc32).CopyTo(finalizePayload, 4);
        finalizePayload[8] = version.Major;
        finalizePayload[9] = version.Minor;
        finalizePayload[10] = version.Patch;
        SendAndExpectOk(port, BootProtocol.Cmd.Finalize, finalizePayload, timeoutMs: 3000,
            "FINALIZE 실패 - CRC 불일치 또는 메타데이터 저장 오류입니다.");

        // 5단계: 재부팅 요청. 응답은 참고만 하고(장치가 곧 리셋되어 연결이
        // 끊기는 게 정상이므로), 응답이 없어도 실패로 취급하지 않는다.
        Log("재부팅 요청 전송...");
        port.Write(BootProtocol.BuildFrame(BootProtocol.Cmd.Reboot, Array.Empty<byte>()), 0, 7);
        _ = BootProtocol.ReadFrame(port.BaseStream, 500);

        Log("업데이트 완료. 장치가 재부팅되어 새 펌웨어로 동작합니다.");
        ProgressChanged?.Invoke(100);
    }

    /// <summary>
    /// 장치가 이미 부트로더 모드면 바로 PING하고, 아니면(앱 정상 동작 중)
    /// ENTER_UPDATE를 보내 재부팅시킨 뒤 부트로더로 재열거될 때까지 기다렸다가
    /// PING한다. RunAsync와 CheckStatusAsync가 공통으로 쓰는 연결 절차다.
    /// </summary>
    private async Task<BootProtocol.Frame> ConnectAndPingAsync(string portName, CancellationToken ct)
    {
        BootProtocol.Frame? ping = TryPing(portName, 600);

        if (ping == null)
        {
            Log("장치가 정상 동작 중인 것으로 보입니다 - 업데이트 진입(ENTER_UPDATE) 요청을 보냅니다.");
            SendEnterUpdate(portName);

            Log("장치가 재부팅하고 부트로더 모드로 재열거될 때까지 기다립니다...");
            await Task.Delay(1500, ct);

            ping = WaitForPing(portName, overallTimeoutMs: 8000, ct);
            if (ping == null)
            {
                throw new InvalidOperationException(
                    "부트로더 모드로 진입한 장치를 찾지 못했습니다. USB 케이블/포트를 확인한 뒤 다시 시도해 주세요.");
            }
        }
        else
        {
            Log("장치가 이미 부트로더 모드입니다.");
        }

        return ping;
    }

    /// <summary>
    /// PING 응답 파싱: [status][blMajor][blMinor][blPatch][activeSlot]
    ///                 [slotA_valid][slotA_major][slotA_minor][slotA_patch]
    ///                 [slotB_valid][slotB_major][slotB_minor][slotB_patch]
    ///                 [state][retry_count][slotA_bootable][slotB_bootable]  (진단용, 신버전 부트로더만)
    /// </summary>
    private void LogPingDiagnostics(BootProtocol.Frame ping)
    {
        byte[] p = ping.Payload;
        var activeSlot = (BootProtocol.SlotId)p[4];

        Log($"부트로더 버전 {p[1]}.{p[2]}.{p[3]}, 현재 active_slot={activeSlot}");
        Log($"SlotA: valid={p[5] != 0}, 버전={p[6]}.{p[7]}.{p[8]}");
        Log($"SlotB: valid={p[9] != 0}, 버전={p[10]}.{p[11]}.{p[12]}");

        if (p.Length >= 17)
        {
            string state = p[13] == 0 ? "CONFIRMED" : "PENDING_TEST";
            Log($"[진단] metadata state={state}, retry_count={p[14]}, " +
                $"SlotA 부팅가능(벡터테이블 유효)={p[15] != 0}, SlotB 부팅가능(벡터테이블 유효)={p[16] != 0}");
        }
        else
        {
            Log("[진단] 부트로더가 구버전이라 state/retry_count/부팅가능 여부 필드가 없습니다.");
        }
    }

    private void SendAndExpectOk(SerialPort port, BootProtocol.Cmd cmd, byte[] payload, int timeoutMs,
        string failMessage)
    {
        var frame = BootProtocol.BuildFrame(cmd, payload);
        port.Write(frame, 0, frame.Length);
        var resp = BootProtocol.ReadFrame(port.BaseStream, timeoutMs);
        if (resp == null || !resp.IsOk)
        {
            string detail = resp == null
                ? "응답 없음(타임아웃)"
                : $"FAIL 응답 수신(payload={BitConverter.ToString(resp.Payload)})";
            throw new InvalidOperationException($"{failMessage} [{detail}]");
        }
    }

    private void SendEnterUpdate(string portName)
    {
        using var port = OpenPort(portName);
        var frame = BootProtocol.BuildFrame(BootProtocol.Cmd.EnterUpdate, Array.Empty<byte>());

        // 앱은 이 프레임에 응답하지 않고 곧바로 리셋하므로, 전송 유실에
        // 대비해 짧은 간격으로 몇 번 반복 전송한다(중복 수신되어도 앱은
        // 어차피 리셋 한 번만 하므로 문제없다).
        for (int i = 0; i < 3; i++)
        {
            try
            {
                port.Write(frame, 0, frame.Length);
            }
            catch (Exception)
            {
                // 이미 리셋되어 포트가 끊겼을 수 있음 - 무시.
                break;
            }
            Thread.Sleep(100);
        }
    }

    private BootProtocol.Frame? TryPing(string portName, int timeoutMs)
    {
        try
        {
            using var port = OpenPort(portName);
            var frame = BootProtocol.BuildFrame(BootProtocol.Cmd.Ping, Array.Empty<byte>());
            port.Write(frame, 0, frame.Length);
            return BootProtocol.ReadFrame(port.BaseStream, timeoutMs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private BootProtocol.Frame? WaitForPing(string portName, int overallTimeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(overallTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var result = TryPing(portName, 500);
            if (result != null)
            {
                return result;
            }

            Thread.Sleep(300);
        }
        return null;
    }

    private static SerialPort OpenPort(string portName)
    {
        // USB CDC 가상 COM포트라 실제 baudrate/parity는 무의미하지만(값은
        // 하드웨어로 전달되지 않음), API 상 값을 채워야 하므로 관례적인
        // 값을 넣는다.
        var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 200,
            WriteTimeout = 2000,
        };
        port.Open();
        return port;
    }

    private void Log(string message) => LogMessage?.Invoke(message);
}
