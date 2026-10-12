using OperateTest.App.Models;
using OperateTest.App.Services;
using OperateTest.App.Services.StLink;
using TestEmulator.Controls;
using TestEmulator.Layout;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class RotaryInspectionForm : Form
{
    private readonly ICommRotaryReader _reader;
    private readonly InspectionSession _session;
    private readonly CancellationTokenSource _cts = new();
    private readonly Label[] _positions = new Label[16];
    private readonly HashSet<int> _seen = [];
    private readonly Label _value = new();
    private readonly Label _raw = new();
    private readonly Label _status = new();
    private readonly AppCard _seenCard = new();
    private readonly Label _instruction = new();
    private int _expectedPosition;
    private int? _lastPosition;
    private int _stableReads;
    private readonly InspectionAuditService _audit = new();
    private int _attempt;
    private bool _attemptCompleted;
    private Task? _monitorTask;
    private bool _closing;
    private bool _closeCleanupRunning;

    public RotaryInspectionForm(ICommRotaryReader reader, InspectionSession session)
    {
        _reader = reader;
        _session = session;
        _session.Results.InvalidateFinalization();
        _session.Results.RotarySwitchPassed = null;
        _session.Results.RotaryStatus = InspectionExecutionStatus.Running;
        _attempt = _audit.BeginAttempt(_session, "rotary_switch", "0~F 순차 위치 검사");
        new InspectionCheckpointService().TrySave(_session);
        Text = "로터리 스위치 검사";
        StartPosition = FormStartPosition.CenterParent;
        ResponsiveWindow.Configure(this, new Size(1080, 680), new Size(860, 620), scrollWhenCompact: true);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Colors.Surface };
        header.Controls.Add(new Label { Location = new Point(30, 17), Size = new Size(560, 34), Font = Fonts.PageTitle, ForeColor = Colors.TextPrimary, Text = "COMM 로터리 스위치 검사" });
        header.Controls.Add(new Label { Location = new Point(32, 54), Size = new Size(650, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "ST-LINK로 PC0~PC3을 읽어 물리 스위치 위치를 실시간 표시합니다." });
        var reset = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Refresh, Text = "RESET", Location = new Point(884, 24), Size = new Size(150, 44) };
        var fail = new AppButton { Variant = AppButtonVariant.Danger, IconGlyph = AppIcon.Warning, Text = "FAIL 판정", Location = new Point(720, 24), Size = new Size(150, 44) };
        fail.Click += (_, _) => MarkFailed(); header.Controls.Add(fail);
        reset.Click += (_, _) => ResetHistory(); header.Controls.Add(reset);
        fail.BringToFront();
        reset.BringToFront();
        Controls.Add(header);

        var currentCard = new AppCard { Location = new Point(30, 116), Size = new Size(310, 100), CardLabel = "현재 위치", CardValue = "--", Icon = AppIcon.Settings, IconColor = Colors.Primary, IconSurfaceColor = Colors.SurfaceBlue, ValueColor = Colors.Primary };
        _seenCard.Location = new Point(356, 116); _seenCard.Size = new Size(310, 100); _seenCard.CardLabel = "확인한 위치"; _seenCard.CardValue = "0 / 16"; _seenCard.Icon = AppIcon.Check; _seenCard.IconColor = Colors.Success; _seenCard.IconSurfaceColor = Colors.SuccessSurface; _seenCard.ValueColor = Colors.Success;
        var linkCard = new AppCard { Location = new Point(682, 116), Size = new Size(350, 100), CardLabel = "ST-LINK", CardValue = _reader.CliPath is null ? "CLI 없음" : "연결 확인 중", Icon = AppIcon.Connector, IconColor = Colors.Primary, IconSurfaceColor = Colors.SurfaceBlue, ValueColor = Colors.TextMuted };
        Controls.Add(currentCard); Controls.Add(_seenCard); Controls.Add(linkCard);

        var main = new AppPanel { Location = new Point(30, 240), Size = new Size(660, 390) };
        main.Controls.Add(new Label { Location = new Point(24, 18), Size = new Size(300, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "실시간 위치" });
        _value.Location = new Point(24, 52); _value.Size = new Size(612, 92); _value.Font = new Font("맑은 고딕", 44, FontStyle.Bold); _value.ForeColor = Colors.Primary; _value.TextAlign = ContentAlignment.MiddleCenter; _value.Text = "--";
        _raw.Location = new Point(24, 145); _raw.Size = new Size(612, 26); _raw.Font = Fonts.Body; _raw.ForeColor = Colors.TextSecondary; _raw.TextAlign = ContentAlignment.MiddleCenter; _raw.Text = "PC3 PC2 PC1 PC0: ----";
        main.Controls.Add(_value); main.Controls.Add(_raw);
        var grid = new TableLayoutPanel { Location = new Point(24, 194), Size = new Size(612, 124), ColumnCount = 8, RowCount = 2, BackColor = Color.Transparent };
        for (var i = 0; i < 8; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));
        for (var i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        for (var i = 0; i < 16; i++)
        {
            var label = new Label { Dock = DockStyle.Fill, Margin = new Padding(4), BackColor = Colors.SurfaceBlue, ForeColor = Colors.TextMuted, Font = Fonts.ButtonText, TextAlign = ContentAlignment.MiddleCenter, Text = i.ToString("X1") };
            _positions[i] = label; grid.Controls.Add(label, i % 8, i / 8);
        }
        _positions[0].BackColor = Colors.WarningSurface;
        _positions[0].ForeColor = Colors.Warning;
        main.Controls.Add(grid);
        _status.Location = new Point(24, 333); _status.Size = new Size(612, 34); _status.Font = Fonts.ButtonText; _status.ForeColor = Colors.TextMuted; _status.TextAlign = ContentAlignment.MiddleCenter; _status.Text = "ST-LINK 연결 확인 중...";
        main.Controls.Add(_status); Controls.Add(main);

        var guide = new AppPanel { Location = new Point(714, 240), Size = new Size(318, 390) };
        guide.Controls.Add(new Label { Location = new Point(22, 18), Size = new Size(250, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "단계별 안내" });
        guide.Controls.Add(new Label { Location = new Point(22, 58), Size = new Size(278, 150), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = "1. COMM 보드에 전원을 공급합니다.\n\n2. ST-LINK를 연결합니다.\n\n3. 화면이 지시하는 위치로 로터리를 돌립니다.\n\n4. 값이 안정되면 다음 위치로 자동 진행됩니다." });
        _instruction.SetBounds(22, 226, 272, 58);
        _instruction.Font = Fonts.ButtonText;
        _instruction.ForeColor = Colors.Warning;
        _instruction.BackColor = Colors.WarningSurface;
        _instruction.TextAlign = ContentAlignment.MiddleCenter;
        _instruction.Text = "로터리 스위치를 0 위치로\n설정하세요.";
        guide.Controls.Add(_instruction);
        guide.Controls.Add(new Label { Location = new Point(22, 300), Size = new Size(272, 58), Font = Fonts.Caption, ForeColor = Colors.Primary, BackColor = Colors.SurfaceBlue, TextAlign = ContentAlignment.MiddleCenter, Text = "0부터 F까지 순서대로 확인하며\n두 번 연속 같은 값이면 인정합니다." });
        Controls.Add(guide);

        Shown += (_, _) => _monitorTask = MonitorAsync(currentCard, linkCard);
        FormClosing += OnClosing;
    }

    private async Task MonitorAsync(AppCard currentCard, AppCard linkCard)
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var state = await _reader.ReadAsync(_cts.Token);
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    if (_session.Results.RotaryStatus == InspectionExecutionStatus.Error)
                        _session.Results.RotaryStatus = InspectionExecutionStatus.Running;
                    _value.Text = state.HexPosition; currentCard.CardValue = state.HexPosition;
                    _raw.Text = $"PC3 PC2 PC1 PC0: {Convert.ToString(state.RawHighNibble, 2).PadLeft(4, '0')} (접점 LOW 반전)";
                    AdvanceIfStable(state.Position);
                    linkCard.CardValue = "연결됨"; linkCard.ValueColor = Colors.Success;
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _session.Results.RotaryStatus = InspectionExecutionStatus.Error;
                if (IsDisposed) return;
                BeginInvoke(() => { _value.Text = "--"; currentCard.CardValue = "--"; linkCard.CardValue = "연결 실패"; linkCard.ValueColor = Colors.Danger; _status.Text = ex.Message; _status.ForeColor = Colors.Danger; });
            }
            try { await Task.Delay(500, _cts.Token); } catch (OperationCanceledException) { return; }
        }
    }

    private void ResetHistory()
    {
        CompleteAttempt();
        _session.Results.InvalidateFinalization();
        _seen.Clear(); _expectedPosition = 0; _lastPosition = null; _stableReads = 0;
        _session.Results.RotarySwitchPassed = null;
        _session.Results.RotaryStatus = InspectionExecutionStatus.Running;
        _attempt = _audit.BeginAttempt(_session, "rotary_switch", "검사 이력 초기화 후 재검사");
        _attemptCompleted = false;
        new InspectionCheckpointService().TrySave(_session);
        _seenCard.CardValue = "0 / 16";
        foreach (var label in _positions) { label.BackColor = Colors.SurfaceBlue; label.ForeColor = Colors.TextMuted; }
        _positions[0].BackColor = Colors.WarningSurface; _positions[0].ForeColor = Colors.Warning;
        _instruction.Text = "로터리 스위치를 0 위치로\n설정하세요.";
        _instruction.ForeColor = Colors.Warning; _instruction.BackColor = Colors.WarningSurface;
        _status.Text = "확인 이력을 초기화했습니다."; _status.ForeColor = Colors.TextMuted;
    }

    private void AdvanceIfStable(int position)
    {
        if (_session.Results.RotaryStatus == InspectionExecutionStatus.Fail) return;
        if (_expectedPosition >= 16)
        {
            _status.Text = "0~F 전 위치 확인 완료 · PASS";
            _status.ForeColor = Colors.Success;
            return;
        }

        if (position != _expectedPosition)
        {
            _lastPosition = position;
            _stableReads = 0;
            _status.Text = $"현재 {position:X1} 감지 · {_expectedPosition:X1} 위치로 맞춰주세요.";
            _status.ForeColor = Colors.Warning;
            return;
        }

        _stableReads = _lastPosition == position ? _stableReads + 1 : 1;
        _lastPosition = position;
        _status.Text = $"{position:X1} 위치 안정 확인 중 ({_stableReads}/2)";
        _status.ForeColor = Colors.Primary;
        if (_stableReads < 2) return;

        _seen.Add(position);
        _positions[position].BackColor = Colors.SuccessSurface;
        _positions[position].ForeColor = Colors.Success;
        _seenCard.CardValue = $"{_seen.Count} / 16";
        _expectedPosition++;
        _stableReads = 0;
        _lastPosition = null;
        new InspectionCheckpointService().TrySave(_session);

        if (_expectedPosition >= 16)
        {
            _session.Results.RotarySwitchPassed = true;
            _session.Results.RotaryStatus = InspectionExecutionStatus.Pass;
            CompleteAttempt();
            _instruction.Text = "0~F 전 위치 확인 완료\n검사 결과 PASS";
            _instruction.ForeColor = Colors.Success;
            _instruction.BackColor = Colors.SuccessSurface;
            _status.Text = "0~F 전 위치 확인 완료 · PASS";
            _status.ForeColor = Colors.Success;
            return;
        }

        _positions[_expectedPosition].BackColor = Colors.WarningSurface;
        _positions[_expectedPosition].ForeColor = Colors.Warning;
        _instruction.Text = $"다음: 로터리 스위치를 {_expectedPosition:X1} 위치로\n설정하세요.";
        _status.Text = $"{position:X1} 확인 완료 · 다음 {_expectedPosition:X1}";
        _status.ForeColor = Colors.Success;
    }

    private void MarkFailed()
    {
        if (MessageBox.Show(this, "로터리 스위치 검사를 FAIL로 판정하시겠습니까?", "FAIL 판정 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _session.Results.InvalidateFinalization();
        _session.Results.RotarySwitchPassed = false;
        _session.Results.RotaryStatus = InspectionExecutionStatus.Fail;
        CompleteAttempt();
        _instruction.Text = "검사자 FAIL 판정";
        _instruction.ForeColor = Colors.Danger;
        _instruction.BackColor = Colors.DangerSurface;
        _status.Text = "로터리 스위치 검사 · FAIL";
        _status.ForeColor = Colors.Danger;
        new InspectionCheckpointService().TrySave(_session);
    }

    private void CompleteAttempt()
    {
        if (_attemptCompleted || _attempt == 0) return;
        _audit.CompleteAttempt(_session, "rotary_switch", _attempt, _session.Results.RotaryStatus, _status.Text);
        _attemptCompleted = true;
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (_closeCleanupRunning) return;
        _closeCleanupRunning = true;
        if (_session.Results.RotaryStatus == InspectionExecutionStatus.Running)
            _session.Results.RotaryStatus = InspectionExecutionStatus.Stopped;
        CompleteAttempt();
        _cts.Cancel();
        try
        {
            if (_monitorTask is not null)
                await SafetyOperationService.RunWithTimeoutAsync(
                    "로터리 ST-LINK 감시 종료",
                    SafetyOperationService.DioOutputOffTimeout,
                    ct => _monitorTask.WaitAsync(ct));
        }
        catch (Exception ex)
        {
            _session.Results.RotaryStatus = InspectionExecutionStatus.Error;
            new InspectionAuditService().TryRecord(_session, "safety_cleanup_failed", "rotary_switch", "error", ex.Message, ex);
            MessageBox.Show(this, $"ST-LINK 읽기 작업을 종료하지 못했습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "검사 종료 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        new InspectionCheckpointService().TrySave(_session);
        _closing = true;
        Close();
    }

    protected override void Dispose(bool disposing) { if (disposing) _cts.Dispose(); base.Dispose(disposing); }
}
