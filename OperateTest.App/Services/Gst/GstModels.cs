namespace OperateTest.App.Services.Gst;

/// <summary>GST 프레임의 Function Code(Modbus FC와 무관한 GST 자체 값,
/// COMMUNICATION_SPEC.md 11절/`a_gst_protocol.c:17-18`).</summary>
public static class GstFunctionCode
{
    public const byte Read = 0x01;
    public const byte Write = 0x02;
}

/// <summary>프레임의 송수신 방향. 컨트롤러가 항상 타이밍을 주도하므로(주기 push + pending write
/// 트리거), 이 값 하나로 "우리가 보낸 것"과 "컨트롤러가 보낸 것"을 Transaction 로그에서 구분한다.</summary>
public enum GstFrameDirection
{
    /// <summary>에뮬레이터(Backup Interface/GST peer)가 컨트롤러로 보낸 프레임.</summary>
    Tx,
    /// <summary>컨트롤러가 에뮬레이터로 보낸 프레임(주기적 push 또는 pending write 트리거).</summary>
    Rx
}

/// <summary>수신 프레임 하나의 분류 결과. `a_gst_protocol.c`의 `ValidateFrame`은 성공/실패만
/// 반환하고 실패 종류를 구분하지 않지만, 이 값은 에뮬레이터 쪽 로그/Fault 판정을 위해 실패
/// 종류를 구분한다(실제 컨트롤러가 이 구분을 로그로 남긴다는 뜻은 아님).</summary>
public enum GstTransactionResult
{
    Success,
    HeaderInvalid,
    TailInvalid,
    CrcError,
    FunctionCodeInvalid,
    LengthMismatch,
    PartialFrame,
    NoResponse
}

/// <summary>파싱에 성공한 GST 프레임 하나. Address는 빅엔디안 필드, Registers는 레지스터당
/// 리틀엔디안(Lo,Hi) payload를 이미 정수로 환원한 값이다.</summary>
public sealed record GstFrame(byte FunctionCode, ushort Address, ushort[] Registers, byte[] RawBytes);

/// <summary>Raw 표시/기록용 Transaction 한 건.</summary>
public sealed record GstTransaction(
    DateTime Timestamp,
    GstFrameDirection Direction,
    string RawHex,
    GstTransactionResult Result,
    string Detail);

/// <summary>Backup Interface(GST peer, 에뮬레이터)가 스스로의 송신 프레임에 고의로 넣는 결함 종류.
/// `ai_work/04-features/IF_FAULT_FEATURE_SPEC.md` 3절 "Backup Interface" 항목과
/// `a_gst_protocol.c`의 실제 검증 순서(Header+FC → Length → Tail → CRC)에 근거한다. NoResponse/Delay는
/// 프레임 내용이 아니라 송신 자체의 유무/시점을 바꾸는 것이라 별도 API로 표현한다(<see cref="Gst.IGstPeerService"/>).</summary>
public enum GstFaultKind
{
    Header,
    Tail,
    Crc,
    FunctionCode,
    Address,
    Length,
    /// <summary>Address 필드(빅엔디안)와 Data payload(레지스터당 리틀엔디안)가 서로 반대라는 점을
    /// 검사한다 — 이 값을 지정하면 Data payload를 빅엔디안으로 뒤집어 보낸다.</summary>
    Endian,
    PartialFrame
}

/// <summary>GST Fault 케이스가 컨트롤러의 어떤 타이밍에 개입하는지. 컨트롤러가 타이밍을 주도하므로
/// 이 프로젝트의 다른 Fault(UART4)와 달리 "우리가 무엇에 반응해서 Fault를 냈는가"가 기대 반응을
/// 결정한다(IF_JUDGMENT_RULES.md 2절 원칙을 GST 구조에 맞게 적용).</summary>
public enum GstFaultContext
{
    /// <summary>컨트롤러의 주기적 push(period_ms)에 개입 — 정상이라면 다음 주기에도 컨트롤러가
    /// 계속 정상적으로 push를 이어간다(우리의 결함 프레임이 컨트롤러를 멈추게 하지 않아야 정상).</summary>
    PeriodicPush,
    /// <summary>컨트롤러의 pending write 트리거(app이 Backup Write 그룹에 값을 쓴 경우)에 개입 —
    /// 우리가 무응답/지연/결함 프레임으로 반응하면, 컨트롤러는 `tx_timeout_ms` 후 같은 Write
    /// 프레임을 재전송한다(`ServicePendingTransfer`, 기본 `retry_count=3`).</summary>
    PendingWrite
}

public enum GstFaultVerdict { Pass, Fail, Error, Stopped }

public sealed record GstFaultCase(
    string Name,
    GstFaultContext Context,
    GstFaultKind? Kind,
    bool WithholdResponse,
    int DelayMs,
    string Rationale,
    bool VerifyLinkStatusTransition = false);

/// <summary><paramref name="PendingRetryMaxAttempts"/>/<paramref name="PendingRetryTimeoutMs"/>는
/// PendingWrite 컨텍스트에서 컨트롤러의 실제 "중첩 재시도" 바깥 루프(pending 재큐잉)를 재현하는 데
/// 쓴다 — `a_gst_protocol.c`의 `ServicePendingTransfer`(outer, `pending_attempts`가 `GetRetryCount()`
/// 기본값 3에 도달할 때까지 재큐잉)를 근거로 기본값을 3회/`tx_timeout_ms` 기본값인 50ms로 둔다.
/// `GstProtocol_TxStep`의 inner HAL 재전송 루프(최대 3회, 같은 GetRetryCount() 값 재사용, worst
/// case 3×3=9)는 실제 UART 배선 위에서 매 성공한 프레임이 한 번만 관측되는 물리적 특성상 peer가
/// 구분해서 관측할 수 없어 이 프로젝트는 outer(pending) 루프만 재현한다 — 이 사실은 추측이 아니라
/// 소스 코드 확인 결과이며 그대로 문서화한다.</summary>
public sealed record GstFaultOptions(int TimeoutMs, int RecoveryCount = 2, int PendingRetryMaxAttempts = 3, int PendingRetryTimeoutMs = 200);

/// <summary><paramref name="PendingRetryAttemptsObserved"/>/<paramref name="PendingRetryElapsed"/>는
/// PendingWrite 컨텍스트에서 실제로 관측된 재전송 횟수/경과 시간이다(OPEN_ITEMS_AUDIT.md OI-20/OI-24).
/// PeriodicPush 컨텍스트에서는 재전송 개념이 없어 항상 null/0이다.
/// <paramref name="RecoveryElapsed"/>는 5단계(Recovery)에 실제로 걸린 시간이다.
/// <paramref name="Transactions"/>는 이 케이스 실행 중 <see cref="IGstPeerService"/>가 실제로 주고받은
/// 프레임 전부다 — 이전엔 이 결과 타입에 프레임 필드 자체가 없어 GST Fault 로그에 TX/RX가 항상
/// 비어 있었다(peer는 이미 모든 송수신을 기록하고 있었는데 여기로 이어붙이지 않았을 뿐이다).</summary>
public sealed record GstFaultTestResult(
    GstFaultCase Case,
    GstFaultVerdict Verdict,
    string ReasonCode,
    string Detail,
    int PendingRetryAttemptsObserved = 0,
    TimeSpan? PendingRetryElapsed = null,
    TimeSpan? RecoveryElapsed = null,
    IReadOnlyList<GstTransaction>? Transactions = null,
    GstFaultReadbackOutcome? Readback = null,
    bool? ReactionConfirmed = null,
    bool? RecoveryConfirmed = null);

public sealed record GstLinkStatusObservation(ushort RawValue, bool Online);

public sealed record GstFaultReadbackOutcome(
    GstLinkStatusObservation? InitialLink,
    GstLinkStatusObservation? OfflineLink,
    GstLinkStatusObservation? RecoveredLink,
    ushort[]? BaselineRegisters,
    ushort[]? ExpectedFaultRegisters,
    ushort[]? ActualFaultRegisters,
    ushort[]? ActualRecoveredRegisters,
    bool FaultEffectConfirmed,
    bool RecoveryConfirmed,
    string Detail);
