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
    private readonly AppButton _prepare;
    private readonly AppButton _open;
    private readonly AppButton _confirmOpen;
    private readonly AppButton _close;
    private readonly AppButton _confirmClose;
    private readonly AppButton _finish;
    private readonly AppButton _fail;
    private readonly AppCard _stageCard;
    private readonly AppCard _faultCard;
    private bool _openConfirmed;
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
        ResponsiveWindow.Configure(this, new Size(1180, 720), new Size(920, 640), scrollWhenCompact: true);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Colors.Surface };
        header.Controls.Add(new Label { Location = new Point(30, 16), Size = new Size(600, 34), Font = Fonts.PageTitle, ForeColor = Colors.TextPrimary, Text = "COMM FAULT 접점 검사" });
        header.Controls.Add(new Label { Location = new Point(32, 53), Size = new Size(760, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "Heating Jacket ID20 응답 차단·복구로 TB18 FAULT 접점 동작을 확인합니다." });
        header.Controls.Add(new StatusBadge { Location = new Point(1000, 31), Status = AppStatus.NotRun });
        Controls.Add(header);

        _stageCard = new AppCard { Location = new Point(30, 116), Size = new Size(350, 100), CardLabel = "현재 단계", CardValue = "검사 대기", Icon = AppIcon.Clipboard, IconColor = Colors.Primary, IconSurfaceColor = Colors.SurfaceBlue, ValueColor = Colors.TextPrimary };
        _faultCard = new AppCard { Location = new Point(396, 116), Size = new Size(350, 100), CardLabel = "FAULT 상태", CardValue = "NORMAL", Icon = AppIcon.Shield, IconColor = Colors.Success, IconSurfaceColor = Colors.SuccessSurface, ValueColor = Colors.Success };
        var targetCard = new AppCard { Location = new Point(762, 116), Size = new Size(370, 100), CardLabel = "측정 위치", CardValue = "TB18-1 ↔ TB18-2", Icon = AppIcon.Connector, IconColor = Colors.Warning, IconSurfaceColor = Colors.WarningSurface, ValueColor = Colors.TextPrimary };
        Controls.Add(_stageCard); Controls.Add(_faultCard); Controls.Add(targetCard);

        var steps = new AppPanel { Location = new Point(30, 240), Size = new Size(520, 420) };
        steps.Controls.Add(new Label { Location = new Point(24, 18), Size = new Size(300, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "검사 순서" });
        steps.Controls.Add(new Label { Location = new Point(24, 50), Size = new Size(450, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "번호 순서대로 실행하고 테스터 측정 결과를 확인합니다." });
        _prepare = Step("1", "정상 통신 준비", 92);
        _open = Step("2", "OPEN (FAULT)", 146);
        _confirmOpen = Step("3", "TB18 개방 확인", 200);
        _close = Step("4", "CLOSE (NORMAL)", 254);
        _confirmClose = Step("5", "TB18 도통 확인", 308);
        _fail = new AppButton { Variant = AppButtonVariant.Danger, IconGlyph = AppIcon.Warning, Text = "FAIL 판정", Location = new Point(24, 364), Size = new Size(224, 40) };
        _finish = new AppButton { Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.Refresh, Text = "검사 종료 · 정상 복구", Location = new Point(260, 364), Size = new Size(236, 40) };
        _open.Enabled = _confirmOpen.Enabled = _close.Enabled = _confirmClose.Enabled = _finish.Enabled = false;
        _fail.Enabled = false;
        steps.Controls.Add(_prepare); steps.Controls.Add(_open); steps.Controls.Add(_confirmOpen); steps.Controls.Add(_close); steps.Controls.Add(_confirmClose); steps.Controls.Add(_fail); steps.Controls.Add(_finish);
        Controls.Add(steps);

        var measurement = new AppPanel { Location = new Point(574, 240), Size = new Size(558, 420) };
        measurement.Controls.Add(new Label { Location = new Point(24, 18), Size = new Size(300, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "테스터 확인 안내" });
        measurement.Controls.Add(new Label { Location = new Point(24, 58), Size = new Size(510, 98), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = "OPEN 단계\nTB18-1 EXT_COM과 TB18-2 EXT_FLT가 개방되는지 확인합니다.\n\nCLOSE 단계\n같은 두 단자가 다시 도통되는지 확인합니다." });
        var info = new AppPanel { Location = new Point(24, 174), Size = new Size(510, 88), BackColor = Colors.SurfaceBlue };
        info.Controls.Add(new Label { Location = new Point(18, 14), Size = new Size(470, 60), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "FAULT 검사 중 MAIN, DIO, Gas, Backup과 나머지 Heating 통신은 유지됩니다.\nOPEN 단계에서 Heating ID20 응답만 차단하고 CLOSE 단계에서 복구합니다." });
        measurement.Controls.Add(info);
        _status.Location = new Point(24, 280); _status.Size = new Size(510, 112); _status.Font = Fonts.ButtonText; _status.ForeColor = Colors.TextMuted; _status.TextAlign = ContentAlignment.MiddleLeft; _status.Text = "MAIN/HJ/GD/Backup 포트를 연결한 뒤 정상 통신 준비를 실행하세요.";
        measurement.Controls.Add(_status); Controls.Add(measurement);

        _prepare.Click += async (_, _) => await PrepareAsync();
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
        SetBusy(); ResetOperation(); ShowState("정상 통신 확인 중", "NORMAL", Colors.Primary);
        try
        {
            await _host.PrepareNormalAsync(_operation!.Token);
            ShowState("정상 통신 준비 완료", "NORMAL", Colors.Success);
            _status.Text = "정상 통신이 유지되고 있습니다.\nTB18 도통 상태를 확인한 뒤 OPEN (FAULT)를 누르세요.";
            _open.Enabled = _finish.Enabled = _fail.Enabled = true;
        }
        catch (OperationCanceledException) { if (_session is not null) { _session.Results.FaultStatus = InspectionExecutionStatus.Stopped; new InspectionCheckpointService().TrySave(_session); } ResetButtons(); }
        catch (Exception ex) { if (_session is not null) { _session.Results.FaultStatus = InspectionExecutionStatus.Error; new InspectionCheckpointService().TrySave(_session); } _status.Text = $"정상 통신 준비 실패\n{ex.Message}"; _status.ForeColor = Colors.Danger; await TryRestoreAndStopAsync("정상 통신 준비 실패 후 정리", showMessage: false); ResetButtons(); }
    }

    private async Task OpenAsync()
    {
        SetBusy(); ShowState("FAULT 유도 중", "전환 중", Colors.Warning); _status.Text = "Heating ID20 응답을 차단하고 MAIN FAULT 판정을 기다립니다...";
        try
        {
            await _host.InduceSlaveFaultAsync(_operation?.Token ?? _lifetime.Token);
            ShowState("OPEN 측정 대기", "OPEN", Colors.Danger); _status.Text = "테스터로 TB18-1과 TB18-2가 개방됐는지 확인하세요."; _status.ForeColor = Colors.Danger;
            _confirmOpen.Enabled = _finish.Enabled = _fail.Enabled = true;
        }
        catch (OperationCanceledException) { if (_session is not null) _session.Results.FaultStatus = InspectionExecutionStatus.Stopped; }
        catch (Exception ex) when (ex is not OperationCanceledException) { if (_session is not null) { _session.Results.FaultPassed = null; _session.Results.FaultStatus = InspectionExecutionStatus.Error; new InspectionCheckpointService().TrySave(_session); } _status.Text = $"FAULT 유도 실패\n{ex.Message}"; _status.ForeColor = Colors.Danger; _open.Enabled = _finish.Enabled = true; }
    }

    private void ConfirmOpen()
    {
        _openConfirmed = true; _confirmOpen.Enabled = false; _close.Enabled = _finish.Enabled = true;
        ShowState("OPEN 확인 완료", "OPEN", Colors.Success); _status.Text = "TB18 개방 확인 완료\nCLOSE (NORMAL)를 누르면 Heating ID20 응답을 복구합니다."; _status.ForeColor = Colors.Success;
    }

    private async Task CloseAsync()
    {
        SetBusy(); ShowState("정상 통신 복구 중", "전환 중", Colors.Primary); _status.Text = "Heating ID20 응답 복구와 정상 통신 유지 여부를 확인하는 중...";
        try
        {
            await SafetyOperationService.RunWithTimeoutAsync(
                "FAULT 정상 통신 복구",
                SafetyOperationService.FaultRestoreTimeout,
                ct => _host.RestoreNormalAsync(ct),
                _operation?.Token ?? _lifetime.Token);
            ShowState("CLOSE 측정 대기", "NORMAL", Colors.Primary); _status.Text = "테스터로 TB18-1과 TB18-2가 도통되는지 확인하세요."; _confirmClose.Enabled = _finish.Enabled = _fail.Enabled = true;
        }
        catch (OperationCanceledException) { if (_session is not null) _session.Results.FaultStatus = InspectionExecutionStatus.Stopped; }
        catch (Exception ex) when (ex is not OperationCanceledException) { if (_session is not null) { _session.Results.FaultPassed = null; _session.Results.FaultStatus = InspectionExecutionStatus.Error; new InspectionCheckpointService().TrySave(_session); } _status.Text = $"정상 복구 실패\n{ex.Message}"; _status.ForeColor = Colors.Danger; _close.Enabled = _finish.Enabled = true; }
    }

    private void ConfirmClose()
    {
        _confirmClose.Enabled = false; _finish.Enabled = true;
        if (_session is not null)
        {
            _session.Results.FaultPassed = _openConfirmed;
            _session.Results.FaultStatus = _openConfirmed ? InspectionExecutionStatus.Pass : InspectionExecutionStatus.Fail;
            CompleteAttempt();
            new InspectionCheckpointService().TrySave(_session);
        }
        ShowState(_openConfirmed ? "접점 검사 완료" : "OPEN 확인 필요", "NORMAL", _openConfirmed ? Colors.Success : Colors.Danger);
        _status.Text = _openConfirmed ? "FAULT OPEN / NORMAL CLOSE 접점 검사 정상" : "CLOSE는 확인했지만 OPEN 확인이 완료되지 않았습니다.";
        _status.ForeColor = _openConfirmed ? Colors.Success : Colors.Danger;
    }

    private async Task FinishAsync()
    {
        SetBusy(); ShowState("정상 복구 후 종료 중", "NORMAL", Colors.Primary);
        if (await TryRestoreAndStopAsync("검사 종료", showMessage: true))
        {
            ShowState("검사 종료", "NORMAL", Colors.TextMuted);
            _status.Text = "Heating ID20 정상 응답을 확인하고 통신 에뮬레이션을 종료했습니다.";
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
        SetBusy();
        try
        {
            if (await TryRestoreAndStopAsync("FAIL 판정 후 정상 복구", showMessage: true))
            {
                ShowState("검사자 FAIL 판정", "NORMAL", Colors.Danger);
                _status.Text = "FAULT 접점 검사 결과를 FAIL로 저장하고 Heating ID20 정상 응답을 확인했습니다.";
                _status.ForeColor = Colors.Danger;
            }
        }
        catch (Exception ex)
        {
            if (_session is not null) _session.Results.FaultStatus = InspectionExecutionStatus.Error;
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
    private void SetBusy() => _prepare.Enabled = _open.Enabled = _confirmOpen.Enabled = _close.Enabled = _confirmClose.Enabled = _fail.Enabled = _finish.Enabled = false;
    private void ResetButtons() { _openConfirmed = false; _prepare.Enabled = true; _open.Enabled = _confirmOpen.Enabled = _close.Enabled = _confirmClose.Enabled = _fail.Enabled = _finish.Enabled = false; }

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
            _status.ForeColor = Colors.Danger;
            if (showMessage)
                MessageBox.Show(this,
                    $"FAULT 정상 복구를 확인하지 못했습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}보드 전원과 TB18 접점 상태를 직접 확인하세요.",
                    "FAULT 안전 종료 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
