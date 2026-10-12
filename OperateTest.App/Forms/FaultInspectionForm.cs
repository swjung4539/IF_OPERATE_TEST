using OperateTest.App.Models;
using OperateTest.App.Services;
using OperateTest.App.Services.CommInspection;
using TestEmulator.Controls;
using TestEmulator.Layout;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class FaultInspectionForm : Form
{
    private readonly ICommEmulationHost _host;
    private readonly InspectionSession? _session;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private readonly Label _status = new();
    private readonly AppButton _open;
    private readonly AppButton _confirmOpen;
    private readonly AppButton _close;
    private readonly AppButton _confirmClose;
    private readonly AppButton _finish;
    private readonly AppButton _fail;
    private readonly AppCard _stageCard;
    private readonly AppCard _faultCard;
    private readonly StatusBadge _verdict = new();
    private bool _openConfirmed;
    private bool _prepared;
    private bool _closing;
    private bool _closeCleanupRunning;
    private readonly InspectionAuditService _audit = new();
    private int _attempt;
    private bool _attemptCompleted;

    public FaultInspectionForm(ISerialConnectionManager connections, IInspectionProtocolServices protocols, ICommEmulationHost? host = null, InspectionSession? session = null)
    {
        _host = host ?? new CommEmulationHost(connections, protocols);
        _session = session;
        Text = "FAULT 접점 검사";
        StartPosition = FormStartPosition.CenterParent;
        ResponsiveWindow.Configure(this, new Size(1180, 760), new Size(920, 680), scrollWhenCompact: true);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Colors.Surface };
        header.Controls.Add(new Label { Location = new Point(30, 12), Size = new Size(650, 42), Font = Fonts.PageTitle, ForeColor = Colors.TextPrimary, Text = "COMM FAULT 접점 검사" });
        header.Controls.Add(new Label { Location = new Point(32, 56), Size = new Size(760, 30), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "TB18-1 ↔ TB18-2 개방 및 도통 확인" });
        _verdict.Location = new Point(1000, 31);
        _verdict.Status = AppStatus.NotRun;
        header.Controls.Add(_verdict);
        Controls.Add(header);

        _stageCard = new AppCard { Location = new Point(30, 116), Size = new Size(350, 100), CardLabel = "현재 단계", CardValue = "검사 대기", Icon = AppIcon.Clipboard, IconColor = Colors.Primary, IconSurfaceColor = Colors.SurfaceBlue, ValueColor = Colors.TextPrimary };
        _faultCard = new AppCard { Location = new Point(396, 116), Size = new Size(350, 100), CardLabel = "FAULT 유도 상태", CardValue = "대기", Icon = AppIcon.Shield, IconColor = Colors.Success, IconSurfaceColor = Colors.SuccessSurface, ValueColor = Colors.Success };
        var targetCard = new AppCard { Location = new Point(762, 116), Size = new Size(370, 100), CardLabel = "측정 위치", CardValue = "TB18-1 ↔ TB18-2", Icon = AppIcon.Connector, IconColor = Colors.Warning, IconSurfaceColor = Colors.WarningSurface, ValueColor = Colors.TextPrimary };
        Controls.Add(_stageCard); Controls.Add(_faultCard); Controls.Add(targetCard);

        var steps = new AppPanel { Location = new Point(30, 240), Size = new Size(520, 420) };
        steps.Controls.Add(new Label { Location = new Point(24, 18), Size = new Size(300, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "검사 순서" });
        steps.Controls.Add(new Label { Location = new Point(24, 50), Size = new Size(450, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "번호 순서대로 실행하고 테스터 측정 결과를 확인합니다." });
        _open = Step("1", "OPEN (FAULT)", 92);
        _confirmOpen = Step("2", "TB18 개방 확인", 146);
        _close = Step("3", "CLOSE (NORMAL)", 200);
        _confirmClose = Step("4", "TB18 도통 확인", 254);
        _fail = new AppButton { Variant = AppButtonVariant.Danger, IconGlyph = AppIcon.Warning, Text = "FAIL 판정", Location = new Point(24, 364), Size = new Size(224, 40) };
        _finish = new AppButton { Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.Refresh, Text = "검사 종료 · 정상 복구", Location = new Point(260, 364), Size = new Size(236, 40) };
        _open.Enabled = true;
        _confirmOpen.Enabled = _close.Enabled = _confirmClose.Enabled = _finish.Enabled = false;
        _fail.Enabled = false;
        steps.Controls.Add(_open); steps.Controls.Add(_confirmOpen); steps.Controls.Add(_close); steps.Controls.Add(_confirmClose); steps.Controls.Add(_fail); steps.Controls.Add(_finish);
        Controls.Add(steps);

        var measurement = new AppPanel { Location = new Point(574, 240), Size = new Size(558, 420) };
        measurement.Controls.Add(new Label { Location = new Point(24, 18), Size = new Size(300, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "테스터 확인 안내" });
        measurement.Controls.Add(new Label { Location = new Point(24, 58), Size = new Size(510, 124), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = "OPEN: TB18-1과 TB18-2의 개방 확인\n\nCLOSE: TB18-1과 TB18-2의 도통 확인" });
        _status.Location = new Point(24, 208); _status.Size = new Size(510, 166); _status.Font = Fonts.ButtonText; _status.ForeColor = Colors.TextMuted; _status.TextAlign = ContentAlignment.TopLeft; _status.Text = "검사 대기 · 판정 없음\nOPEN을 누르세요.";
        measurement.Controls.Add(_status); Controls.Add(measurement);

        _open.Click += async (_, _) => await OpenAsync();
        _confirmOpen.Click += (_, _) => ConfirmOpen();
        _close.Click += async (_, _) => await CloseAsync();
        _confirmClose.Click += (_, _) => ConfirmClose();
        _finish.Click += async (_, _) => await FinishAsync();
        _fail.Click += async (_, _) => await MarkFailedAsync();
        FormClosing += OnClosing;
    }

    private static AppButton Step(string number, string text, int y) => new() { Variant = AppButtonVariant.OutlinePrimary, Text = $"{number}.  {text}", Location = new Point(24, y), Size = new Size(472, 42) };

    private async Task PrepareAsync()
    {
        if (_session is not null)
        {
            if (_attempt > 0 && !_attemptCompleted) CompleteAttempt();
            _session.Results.InvalidateFinalization();
            _session.Results.FaultPassed = null;
            _session.Results.FaultStatus = InspectionExecutionStatus.Running;
            _attempt = _audit.BeginAttempt(_session, "fault_contact", "FAULT OPEN/CLOSE 접점 검사");
            _attemptCompleted = false;
            new InspectionCheckpointService().TrySave(_session);
        }
        SetBusy(); ResetOperation(); _verdict.Status = AppStatus.Running; ShowState("준비 중", "NORMAL", Colors.Primary);
        try
        {
            await _host.PrepareNormalAsync(_operation!.Token);
            _prepared = true;
            ShowState("OPEN 진행 중", "NORMAL", Colors.Primary);
            _status.Text = "준비 완료 · OPEN 실행 중";
            _open.Enabled = _finish.Enabled = _fail.Enabled = true;
        }
        catch (OperationCanceledException) { if (_session is not null) { _session.Results.FaultStatus = InspectionExecutionStatus.Stopped; new InspectionCheckpointService().TrySave(_session); } _verdict.Status = AppStatus.Stopped; _status.Text = "검사 중단 · 판정 없음"; ResetButtons(); }
        catch (Exception ex) { if (_session is not null) { _session.Results.FaultStatus = InspectionExecutionStatus.Error; new InspectionCheckpointService().TrySave(_session); } _verdict.Status = AppStatus.ErrorState; _status.Text = $"검사 오류 · 준비 실패\n{ex.Message}"; _status.ForeColor = Colors.Danger; await TryRestoreAndStopAsync("Heating ID20 준비 실패 후 정리", showMessage: false); ResetButtons(); }
    }

    private async Task OpenAsync()
    {
        if (!_prepared)
        {
            await PrepareAsync();
            if (!_prepared) return;
        }
        SetBusy(); _verdict.Status = AppStatus.Running; ShowState("OPEN 진행 중", "전환 중", Colors.Warning); _status.Text = "OPEN 실행 중 · 판정 대기";
        try
        {
            await _host.InduceSlaveFaultAsync(_operation?.Token ?? _lifetime.Token);
            ShowState("OPEN 확인 대기", "OPEN", Colors.Danger); _status.Text = "OPEN 실행 완료 · TB18 개방을 확인하세요.\n전체 검사: 진행 중 · 판정 대기"; _status.ForeColor = Colors.Danger;
            _confirmOpen.Enabled = _finish.Enabled = _fail.Enabled = true;
        }
        catch (OperationCanceledException) { if (_session is not null) _session.Results.FaultStatus = InspectionExecutionStatus.Stopped; _verdict.Status = AppStatus.Stopped; _status.Text = "검사 중단 · 판정 없음"; }
        catch (Exception ex) when (ex is not OperationCanceledException) { if (_session is not null) { _session.Results.FaultPassed = null; _session.Results.FaultStatus = InspectionExecutionStatus.Error; new InspectionCheckpointService().TrySave(_session); } _verdict.Status = AppStatus.ErrorState; _status.Text = $"검사 오류 · OPEN 실패\n{ex.Message}"; _status.ForeColor = Colors.Danger; _open.Enabled = _finish.Enabled = true; }
    }

    private void ConfirmOpen()
    {
        _openConfirmed = true; _confirmOpen.Enabled = false; _close.Enabled = _finish.Enabled = true;
        ShowState("OPEN 확인 완료", "OPEN", Colors.Success); _status.Text = "OPEN 단계: 확인 완료\n전체 검사: 진행 중 · CLOSE를 누르세요."; _status.ForeColor = Colors.Success;
    }

    private async Task CloseAsync()
    {
        SetBusy(); ShowState("CLOSE 진행 중", "전환 중", Colors.Primary); _status.Text = "CLOSE 실행 중 · 판정 대기";
        try
        {
            await SafetyOperationService.RunWithTimeoutAsync(
                "FAULT Heating ID20 복구",
                SafetyOperationService.FaultRestoreTimeout,
                ct => _host.RestoreNormalAsync(ct),
                _operation?.Token ?? _lifetime.Token);
            ShowState("CLOSE 확인 대기", "NORMAL", Colors.Primary); _status.Text = "CLOSE 실행 완료 · TB18 도통을 확인하세요.\n전체 검사: 진행 중 · 판정 대기"; _confirmClose.Enabled = _finish.Enabled = _fail.Enabled = true;
        }
        catch (OperationCanceledException) { if (_session is not null) _session.Results.FaultStatus = InspectionExecutionStatus.Stopped; _verdict.Status = AppStatus.Stopped; _status.Text = "검사 중단 · 판정 없음"; }
        catch (Exception ex) when (ex is not OperationCanceledException) { if (_session is not null) { _session.Results.FaultPassed = null; _session.Results.FaultStatus = InspectionExecutionStatus.Error; new InspectionCheckpointService().TrySave(_session); } _verdict.Status = AppStatus.ErrorState; _status.Text = $"검사 오류 · CLOSE 실패\n{ex.Message}"; _status.ForeColor = Colors.Danger; _close.Enabled = _finish.Enabled = true; }
    }

    private void ConfirmClose()
    {
        _confirmClose.Enabled = false; _finish.Enabled = true;
        _verdict.Status = _openConfirmed ? AppStatus.Pass : AppStatus.Fail;
        if (_session is not null)
        {
            _session.Results.FaultPassed = _openConfirmed;
            _session.Results.FaultStatus = _openConfirmed ? InspectionExecutionStatus.Pass : InspectionExecutionStatus.Fail;
            CompleteAttempt();
            new InspectionCheckpointService().TrySave(_session);
        }
        ShowState(_openConfirmed ? "접점 검사 완료" : "OPEN 확인 필요", "NORMAL", _openConfirmed ? Colors.Success : Colors.Danger);
        _status.Text = _openConfirmed ? "검사 완료 · PASS\nOPEN 개방 / CLOSE 도통 확인" : "검사 완료 · FAIL\nOPEN 확인이 완료되지 않았습니다.";
        _status.ForeColor = _openConfirmed ? Colors.Success : Colors.Danger;
    }

    private async Task FinishAsync()
    {
        SetBusy(); ShowState("정상 복구 후 종료 중", "NORMAL", Colors.Primary);
        if (await TryRestoreAndStopAsync("검사 종료", showMessage: true))
        {
            ShowState("검사 종료", "NORMAL", Colors.TextMuted);
            if (_verdict.Status == AppStatus.Running) _verdict.Status = AppStatus.Stopped;
            _status.Text = _verdict.Status == AppStatus.Pass ? "검사 종료 · PASS" : _verdict.Status == AppStatus.Fail ? "검사 종료 · FAIL" : "검사 종료 · 판정 없음";
        }
        ResetButtons();
    }

    private async Task MarkFailedAsync()
    {
        if (MessageBox.Show(this, "FAULT 접점 검사를 FAIL로 판정하고 출력을 정상 복구하시겠습니까?", "FAIL 판정 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (_session is not null)
        {
            _session.Results.InvalidateFinalization();
            _session.Results.FaultPassed = false;
            _session.Results.FaultStatus = InspectionExecutionStatus.Fail;
            CompleteAttempt();
            new InspectionCheckpointService().TrySave(_session);
        }
        _verdict.Status = AppStatus.Fail;
        SetBusy();
        try
        {
            if (await TryRestoreAndStopAsync("FAIL 판정 후 정상 복구", showMessage: true))
            {
                ShowState("검사자 FAIL 판정", "NORMAL", Colors.Danger);
                _status.Text = "검사 종료 · FAIL";
                _status.ForeColor = Colors.Danger;
            }
        }
        catch (Exception ex)
        {
            if (_session is not null) _session.Results.FaultStatus = InspectionExecutionStatus.Error;
            _verdict.Status = AppStatus.ErrorState;
            _status.Text = $"FAIL 판정 후 정상 복구 오류\n{ex.Message}";
            _status.ForeColor = Colors.Danger;
        }
        ResetButtons();
    }

    private void ShowState(string stage, string fault, Color color)
    {
        _stageCard.CardValue = stage; _stageCard.ValueColor = color; _faultCard.CardValue = fault; _faultCard.ValueColor = color;
    }

    private void ResetOperation()
    {
        _operation?.Cancel(); _operation?.Dispose(); _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    }
    private void SetBusy() => _open.Enabled = _confirmOpen.Enabled = _close.Enabled = _confirmClose.Enabled = _fail.Enabled = _finish.Enabled = false;
    private void ResetButtons() { _openConfirmed = _prepared = false; _open.Enabled = true; _confirmOpen.Enabled = _close.Enabled = _confirmClose.Enabled = _fail.Enabled = _finish.Enabled = false; }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (_closeCleanupRunning) return;
        _closeCleanupRunning = true;
        _operation?.Cancel();
        if (_session is not null && _session.Results.FaultStatus == InspectionExecutionStatus.Running)
            _session.Results.FaultStatus = InspectionExecutionStatus.Stopped;
        CompleteAttempt();
        if (_session is not null) new InspectionCheckpointService().TrySave(_session);
        await TryRestoreAndStopAsync("창 닫기", showMessage: true);
        _lifetime.Cancel(); _closing = true; Close();
    }

    protected override void Dispose(bool disposing) { if (disposing) { _operation?.Dispose(); _lifetime.Dispose(); } base.Dispose(disposing); }

    private void CompleteAttempt()
    {
        if (_session is null || _attemptCompleted || _attempt == 0) return;
        _audit.CompleteAttempt(_session, "fault_contact", _attempt, _session.Results.FaultStatus, _status.Text);
        _attemptCompleted = true;
    }

    private async Task<bool> TryRestoreAndStopAsync(string context, bool showMessage)
    {
        try
        {
            await SafetyOperationService.RunWithTimeoutAsync(
                "FAULT 정상 복구 및 통신 종료",
                SafetyOperationService.FaultRestoreTimeout,
                async ct =>
                {
                    Exception? restoreError = null;
                    try
                    {
                        if (_host.IsRunning && _host.FaultTargetSuppressed)
                            await _host.RestoreNormalAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        restoreError = ex;
                    }

                    await _host.StopAsync(restoreNormal: false, ct: ct);
                    if (restoreError is not null) throw restoreError;
                });
            if (_session is not null)
                _audit.TryRecord(_session, "safety_cleanup_completed", "fault_restore", "success", $"{context}: 정상 응답 복구 및 종료 완료");
            return true;
        }
        catch (Exception ex)
        {
            if (_session is not null)
            {
                _session.Results.FaultStatus = InspectionExecutionStatus.Error;
                _audit.TryRecord(_session, "safety_cleanup_failed", "fault_restore", "error", $"{context}: {ex.Message}", ex);
                new InspectionCheckpointService().TrySave(_session);
            }
            _status.Text = $"정상 복구 실패\n{ex.Message}\n보드 전원과 TB18 상태를 직접 확인하세요.";
            _verdict.Status = AppStatus.ErrorState;
            _status.ForeColor = Colors.Danger;
            if (showMessage)
                MessageBox.Show(this,
                    $"FAULT 정상 복구를 확인하지 못했습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}보드 전원과 TB18 접점 상태를 직접 확인하세요.",
                    "FAULT 안전 종료 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
