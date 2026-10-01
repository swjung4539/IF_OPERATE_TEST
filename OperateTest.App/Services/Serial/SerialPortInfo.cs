namespace OperateTest.App.Services.Serial;

/// <summary>COM 포트 검색 결과 1건. 내부 연결에는 항상 <see cref="PortName"/>(예: "COM3")을 쓰고,
/// 화면 표시에는 <see cref="DisplayName"/>을 쓴다. Friendly Name 조회에 실패하면
/// <see cref="DisplayName"/>은 <see cref="PortName"/>과 동일한 값이 된다(COMMUNICATION_SCOPE_AUDIT.md
/// "COM 포트 Friendly Name" 항목).</summary>
public sealed record SerialPortInfo(string PortName, string DisplayName);
