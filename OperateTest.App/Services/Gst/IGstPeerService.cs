namespace OperateTest.App.Services.Gst;

/// <summary>Backup Interface(GST peer) 계층. USART1/RS422로 컨트롤러와 마주보며, 컨트롤러가 항상
/// 타이밍을 주도한다(주기적 push + pending write 트리거) — 이 프로젝트의 다른 채널(에뮬레이터가
/// Master/능동 쪽인 UART4/DIO/Heating/Gas)과 반대 역할이다. 에뮬레이터는 컨트롤러가 보내는 프레임을
/// 수신/파싱하고(GST Write 관측), 스스로의 상태 프레임을 송신한다(GST Read — 컨트롤러의 Read
/// 영역 HR242~282에 반영됨, `ApplyReadFrame`).</summary>
public interface IGstPeerService
{
    IReadOnlyList<GstTransaction> Transactions { get; }
    event EventHandler<GstTransaction>? TransactionRecorded;
    void ClearTransactions();

    /// <summary>검사 항목 경계에서 이전 부분 프레임 재조립 상태를 폐기한다.</summary>
    Task ResetReceiveStateAsync(CancellationToken ct = default);

    /// <summary>컨트롤러가 보내는 프레임 하나를 수신/파싱한다(주기적 push 또는 pending write 트리거).
    /// 여러 조각으로 나뉘어 도착해도 재조립하며(200ms 재조립 timeout, `GST_RX_REASSEMBLY_TIMEOUT_MS`와
    /// 동일 값), Header가 아닌 바이트로 시작하면 유효한 Header를 찾을 때까지 앞부분을 버린다
    /// (`GstProtocol_RxStep`의 header sync 로직과 동일).</summary>
    Task<GstFrame> ReceiveFromControllerAsync(int timeoutMs, CancellationToken ct = default);

    /// <summary>Fault 없이 정상 상태 프레임(FC=Read)을 컨트롤러로 보낸다.</summary>
    Task SendStatusAsync(ushort[] registers, CancellationToken ct = default);

    /// <summary><see cref="GstFaultKind"/>대로 변형한 프레임을 보낸다.</summary>
    Task SendFaultFrameAsync(ushort[] registers, GstFaultKind kind, CancellationToken ct = default);

    /// <summary>정상 프레임을 지정한 시간만큼 늦게 보낸다(지연 Fault).</summary>
    Task SendDelayedAsync(ushort[] registers, int delayMs, CancellationToken ct = default);
}
