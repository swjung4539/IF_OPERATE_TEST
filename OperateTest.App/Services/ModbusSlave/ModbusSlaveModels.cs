namespace OperateTest.App.Services.ModbusSlave;

/// <summary>IF_main이 자신의 내부 버스에서 Modbus RTU MASTER로 동작하며 실제 주변장치를 폴링하는
/// 대상 버스. PC가 UART4로 IF_main "자신"을 두드리는 8단계(I/F 정상 통신)와는 완전히 다른 물리적
/// 연결이다 — 여기서는 PC가 그 반대편(주변장치인 척)에 서서 IF_main의 내부 Master 로직을 검사한다.
/// 버스/그룹/응답 timeout/재시도 값은 `a_application.c:428-446`, `a_comm_config.h:11-19,33-39`,
/// `a_modbus_master.c:25-69`에서 확인했다(추측 아님). Slave ID는 실제로는
/// `CommConfig_t.dio_ids/heating_ids/gas_ids`로 런타임 설정되고 코드에 고정값이 없어(`a_comm_config.h:67-69`),
/// 이 프로젝트는 검사 편의를 위한 기본값만 상수로 둔다(실제 배포 시 다를 수 있음 — 확인 필요로 남김).</summary>
public enum ModbusSlaveBus
{
    /// <summary>LPUART1 — DIO. 응답 timeout 기본 50ms.</summary>
    Lpuart1Dio,
    /// <summary>USART2 — Heating. 응답 timeout 기본 200ms.</summary>
    Usart2Heating,
    /// <summary>USART3 — Gas. 응답 timeout 기본 300ms.</summary>
    Usart3Gas
}

/// <summary>Gas Detector 슬롯(ID 30/40/50)에 실제로 어떤 물리 장치가 꽂혀 있는지(REQUIREMENTS 4절 —
/// "Heating/Gas 장치 타입별 폴링 주소가 다른 경우 장치 프로파일을 분리"). `IF_DEVICE_PROFILE_SPEC.md`
/// 5/6절 확인 결과 STD와 TFT는 완전히 다른 FC/주소/Quantity를 쓴다(같은 물리 포트·ID 후보군을
/// 공유하되 컨트롤러의 Gas Type 설정으로 어느 프로파일이 폴링되는지가 갈린다) — 하나의 레지스터
/// Map으로는 둘 다 동시에 만족시킬 수 없어 사용자가 검사 대상 선택 시 프로파일도 함께 고른다.
/// Heating은 모든 유닛(20/21/22)이 동일 프로파일이라 별도 enum이 필요 없다.</summary>
public enum GasDetectorProfile
{
    /// <summary><c>IF_DEVICE_PROFILE_SPEC.md</c> 5절 — Poll FC04 IR30000(4)/FC02 DI10001(8),
    /// Write FC05/0F Coil2·3.</summary>
    Std,
    /// <summary><c>IF_DEVICE_PROFILE_SPEC.md</c> 6절 — Poll FC01 Coil1(4)/FC02 DI0(16)은 코드로
    /// 재확인됐으며, 현재 MAIN a_poll_table.c 기준으로 Input/Holding 1·21·85·89·91 범위도 지원한다.</summary>
    Tft
}

/// <summary>버스별 참고 상수(재시도/응답 timeout/폴링 주기 기본값). 이 값은 IF_main "마스터" 쪽
/// 기본값이고, 우리(Slave 에뮬레이터)의 수신 대기 timeout은 호출자가 별도로 지정한다 — 여기 상수는
/// Fault 케이스의 Rationale에 근거를 남기기 위한 참고용이다.</summary>
public static class ModbusSlaveBusDefaults
{
    public static IReadOnlyList<byte> SlaveIds(ModbusSlaveBus bus) => bus switch
    {
        ModbusSlaveBus.Lpuart1Dio => new byte[] { 1, 2 },
        ModbusSlaveBus.Usart2Heating => new byte[] { 20, 21, 22 },
        ModbusSlaveBus.Usart3Gas => new byte[] { 30, 40, 50 },
        _ => throw new ArgumentOutOfRangeException(nameof(bus))
    };

    public static int ResponseTimeoutMs(ModbusSlaveBus bus) => bus switch
    {
        ModbusSlaveBus.Lpuart1Dio => 50,
        ModbusSlaveBus.Usart2Heating => 200,
        ModbusSlaveBus.Usart3Gas => 300,
        _ => throw new ArgumentOutOfRangeException(nameof(bus))
    };

    /// <summary>`a_comm_config.h:19` GetRetryCount() 기본값 — 세 버스가 공유하는 값.</summary>
    public const int DefaultRetryCount = 3;

    /// <summary>`a_comm_config.h:11-13` 폴링 주기 기본값(세 버스 공통 50ms).</summary>
    public const int DefaultPollPeriodMs = 50;
}

/// <summary>수신한 요청 프레임을 파싱한 결과(Slave 관점 — Master가 아니라 우리가 응답할 차례).</summary>
public sealed record ModbusSlaveRequest(
    byte SlaveId,
    byte FunctionCode,
    ushort Address,
    ushort Quantity,
    ushort[]? WriteRegisterValues,
    bool[]? WriteBitValues,
    byte[] RawBytes);

/// <summary>한 번의 요청 처리 결과 분류.</summary>
public enum ModbusSlaveResponseResult
{
    /// <summary>정상 응답(읽기 결과 또는 쓰기 echo)을 보냄.</summary>
    Success,
    /// <summary>지원하지 않는 Function Code라 Exception(0x01, Illegal Function) 응답을 보냄.</summary>
    ExceptionIllegalFunction,
    /// <summary>구성된 레지스터 범위를 벗어나 Exception(0x02, Illegal Data Address) 응답을 보냄.</summary>
    ExceptionIllegalAddress,
    /// <summary>CRC가 맞지 않아 표준 Modbus RTU 관례대로 아무 응답도 보내지 않음(무시).</summary>
    CrcErrorIgnored,
    /// <summary>Slave ID가 이 에뮬레이터의 것이 아니라 응답하지 않음(같은 버스에 다른 장치가 있다고
    /// 가정 — 응답하지 않는 것이 표준 Modbus RTU 동작).</summary>
    NotAddressedToUs,
    /// <summary>지정한 시간 안에 요청 자체가 전혀 오지 않음.</summary>
    NoRequestReceived,
    /// <summary>Fault 주입 모드로 의도적으로 결함 있는(또는 결여된) 응답을 보냄 —
    /// <see cref="ModbusSlaveFaultKind"/> 참고. 실제 컨트롤러 반응은 확인되지 않았다.</summary>
    FaultInjected
}

/// <summary>요청-응답 교환 한 건의 기록(Raw 표시/로그용). <see cref="ResponseTimeMs"/>는 우리(PC Slave
/// Emulator)가 요청을 다 받은 시점부터 응답(또는 Fault 응답)을 다 보낸 시점까지 걸린 시간이다 — 실제
/// 컨트롤러가 그 응답을 받기까지의 왕복 시간이 아니라 우리 쪽 처리·송신 지연이며, 응답을 아예 보내지
/// 않은 경우(무응답/CRC 무시 등)는 null이다. "응답 지연" Fault 케이스에서는 의도한 지연(DelayMs)이
/// 그대로 이 값에 반영돼 실제로 지연이 걸렸는지 확인할 수 있다.</summary>
public sealed record ModbusSlaveExchange(
    DateTime Timestamp,
    string? RequestHex,
    string? ResponseHex,
    ModbusSlaveResponseResult Result,
    string Detail,
    double? ResponseTimeMs = null);

/// <summary>내부 Modbus 응답 Fault(OPEN_ITEMS_AUDIT.md OI-10) 종류. UART4/GST의 Fault 카탈로그와
/// 같은 원칙(요청이 아니라 우리가 만드는 응답을 고의로 변형)이되, 이번에는 우리가 Slave(응답자)
/// 역할이라 응답 Fault만 존재한다(요청은 컨트롤러가 만들고 우리가 변형할 수 없음).</summary>
public enum ModbusSlaveFaultKind
{
    /// <summary>응답 자체를 보내지 않음(무응답).</summary>
    NoResponse,
    /// <summary>정상 응답을 만들되 지정한 지연 후에 보냄.</summary>
    DelayedResponse,
    /// <summary>정상 응답의 CRC 마지막 바이트를 고의로 훼손.</summary>
    CrcError,
    /// <summary>응답의 Slave ID 필드를 요청과 다른 값으로 바꿔치기.</summary>
    WrongSlaveIdEcho,
    /// <summary>응답의 Function Code를 요청과 다른 값으로 바꿔치기(Exception 비트 없이).</summary>
    WrongFunctionCode,
    /// <summary>읽기 응답의 Byte Count 필드만 실제 데이터 길이와 다르게 조작.</summary>
    WrongByteCount,
    /// <summary>정상 프레임이되 값 하나를 실제 레지스터 값과 다르게 바꿔치기.</summary>
    DataMismatch,
    /// <summary>정상 프레임을 끝에서 잘라 보냄.</summary>
    PartialFrame,
    /// <summary>정상 프레임 뒤에 잉여 바이트를 붙여 보냄.</summary>
    OversizedFrame,
    /// <summary>정상 상황인데도 Exception 응답을 보냄(원래 필요 없는 Exception).</summary>
    UnexpectedException,
    /// <summary>같은 정상 응답을 두 번 연달아 보냄.</summary>
    DuplicateResponse,
    /// <summary>응답을 계속 차단해 IF_main의 요청 재시도와 3-fail 상태 전환을 모두 소진한 뒤,
    /// UART4 슬레이브 상태 레지스터 bit8이 0→1로 전환·복구되는지 교차 검증.</summary>
    RetryExhaustionStatusRecovery
}

/// <summary>Fault 케이스 하나의 정의. <see cref="ExpectedControllerReactionNote"/>는 IF_main
/// 소스 코드(a_modbus_master.c)에서 확인한 "이론상 기대되는" 반응을 참고용으로 남긴다.</summary>
public sealed record ModbusSlaveFaultCase(
    string Name,
    ModbusSlaveBus Bus,
    ModbusSlaveFaultKind Kind,
    string ExpectedControllerReactionNote);

/// <summary>Fault 주입 검사 결과. 처음엔 "실제 컨트롤러 반응은 하드웨어 없이 확인 불가"라는 이유로
/// Pass/Fail 없이 Sent(생성·송신 완료)만 표시했는데, 실사용 결과 "그럼 대체 뭘로 판단하냐"는
/// 정당한 지적을 받았다 — Fault 응답을 보낸 뒤 컨트롤러가 다음 폴링 요청을 정상적으로 다시 보내오는지
/// (<see cref="ModbusSlaveFaultInjectionService"/>의 회복 확인 단계)를 관찰 가능한 판정 기준으로 삼아
/// 실제 Pass/Fail을 매긴다.</summary>
public enum ModbusSlaveFaultVerdict { Pass, Fail, Error, Stopped }

/// <summary>REQUIREMENTS 5절 — UI의 "재시도"/"복구 확인" 값이 이전엔 이 옵션에 전혀 전달되지 않아
/// (필드 자체가 없었다) 화면에 표시된 값과 실제 실행이 서로 무관했다. <see cref="RetryCount"/>는
/// Baseline 단계에서 정상 요청-응답 확인을 몇 번까지 재시도할지(라운드로빈 버스 잡음으로 인한
/// 우연한 실패를 IF_main의 실제 재시도 동작과 같은 방식으로 흡수), <see cref="RecoveryTargetCount"/>는
/// Fault 이후 몇 회 "연속" 정상 요청을 확인해야 PASS로 인정할지(1이면 1회, 3이면 3회 연속)를
/// 결정한다. <see cref="MaxRecoveryAttempts"/>는 그 연속 횟수를 채우기 위해 시도할 수 있는 총
/// 상한(UI에는 노출하지 않음 — UART4 파이프라인의 <see cref="OperateTest.App.Services.If.IfFaultOptions.MaxRecoveryAttempts"/>와
/// 동일한 역할).</summary>
public sealed record ModbusSlaveFaultOptions(
    int WaitForRequestTimeoutMs = 1000,
    int DelayMs = 100,
    int RetryCount = 3,
    int RecoveryTargetCount = 1,
    int MaxRecoveryAttempts = 15);

/// <summary>복구(N회 연속 정상) 판정 결과 — <see cref="OperateTest.App.Services.If.IfRecoveryOutcome"/>과
/// 동일한 개념을 내부 Modbus Slave 파이프라인에 적용한 것. <see cref="Attempts"/>에 시도마다의 실제
/// 요청-응답 교환을 전부 남겨(REQUIREMENTS 5절 "로그에 시도 횟수와 각 시도의 결과를 남겨라") 몇 번째
/// 시도에서 무엇이 왔는지 로그에서 그대로 확인할 수 있게 한다.</summary>
/// <param name="HardError">REQUIREMENTS 8절 — "ERROR와 FAIL을 절대 혼용하지 마라": 복구 확인 중 응답이
/// 없는 것(컨트롤러가 Fault에 반응해 폴링을 멈춤 — FAIL 신호)과, 응답은 왔지만 우리 쪽 등록된 Map과
/// 안 맞는 주소/FC라 Exception이 나온 것(검사 설정 문제 — ERROR 신호)은 원인이 다르다. 후자가 나오면
/// 이 필드가 true가 되고, 호출자(<see cref="ModbusSlaveFaultInjectionService"/>)는 이를 FAIL이 아니라
/// ERROR로 분류해야 한다 — 연속 성공 카운터만 0으로 리셋하고 넘어가면 "컨트롤러 반응이 기대와 다름"
/// (FAIL)과 "이 검사 자체를 정상 수행하지 못함"(ERROR)이 뭉개진다.</param>
public sealed record ModbusSlaveRecoveryOutcome(
    bool Confirmed,
    int ConsecutiveSuccesses,
    int TargetCount,
    int AttemptsUsed,
    IReadOnlyList<ModbusSlaveExchange> Attempts,
    string Detail,
    bool HardError = false);

/// <summary>Fault 응답 직후 Controller가 보인 1차 반응의 판정 결과.</summary>
public sealed record ModbusSlaveReactionOutcome(
    bool RequiresSameRequestRetry,
    bool Confirmed,
    string? FaultRequestHex,
    string? FirstRequestAfterFaultHex,
    string Expected,
    string Actual);

/// <summary>UART4 global HR131~145에서 선택 Slave ID의 상태 레지스터를 찾아 확인한 결과.</summary>
public sealed record ModbusSlaveStatusObservation(
    byte SlaveId,
    ushort RegisterAddress,
    ushort RawValue,
    bool Online);

/// <summary>재시도 소진 검사에서 관측한 SharedMap 슬레이브 상태 전환.</summary>
public sealed record ModbusSlaveStatusTransitionOutcome(
    ModbusSlaveStatusObservation Initial,
    ModbusSlaveStatusObservation? Offline,
    ModbusSlaveStatusObservation? Recovered,
    int FaultResponsesWithheld,
    int StatusReads,
    bool OfflineConfirmed,
    bool RecoveryConfirmed,
    string Detail);

/// <summary>Fault 케이스 하나의 최종 결과. 이전엔 <c>Exchange</c> 하나만 들고 있어서, PASS(가장 흔한
/// 케이스)일 때는 그 다음 정상 복구 왕복만 남고 실제로 주입한 결함 프레임(<see cref="FaultExchange"/>)
/// 자체가 로그에서 사라졌다 — Baseline/Fault/Recovery 세 교환을 각각 보존해서 어떤 판정이든 실제
/// 송수신한 패킷을 로그에서 확인할 수 있게 한다.</summary>
/// <param name="BaselineAttempts">REQUIREMENTS 5절 — Baseline 단계에서 실제로 시도한 요청-응답 교환
/// 전부(재시도 포함). <see cref="Baseline"/>은 그중 성공한(또는 마지막) 시도 하나를 대표로 남긴다.</param>
/// <param name="RecoveryOutcome">REQUIREMENTS 5절 — 복구 확인이 몇 번 시도되고 몇 번 연속 성공했는지.
/// <see cref="Recovery"/>는 그중 마지막 시도 하나를 대표로 남긴다(기존 단일 교환 소비자와의 호환).</param>
public sealed record ModbusSlaveFaultResult(
    ModbusSlaveFaultCase Case,
    ModbusSlaveFaultVerdict Verdict,
    ModbusSlaveExchange? Baseline,
    ModbusSlaveExchange? FaultExchange,
    ModbusSlaveExchange? Recovery,
    string Detail,
    IReadOnlyList<ModbusSlaveExchange>? BaselineAttempts = null,
    ModbusSlaveRecoveryOutcome? RecoveryOutcome = null,
    ModbusSlaveReactionOutcome? ReactionOutcome = null,
    IReadOnlyList<ModbusSlaveExchange>? FaultAttempts = null,
    ModbusSlaveStatusTransitionOutcome? StatusTransition = null);
