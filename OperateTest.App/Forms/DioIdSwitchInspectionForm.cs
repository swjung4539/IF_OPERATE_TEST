using OperateTest.App.Models;
using OperateTest.App.Services;
using OperateTest.App.Services.StLink;
using TestEmulator.Controls;
using TestEmulator.Layout;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class DioIdSwitchInspectionForm : Form
{
    private readonly IDioIdSwitchReader _reader;
    private readonly InspectionSession _session;
    private readonly CancellationTokenSource _cts = new();
    private readonly Label _id = new();
    private readonly Label _pins = new();
    private readonly Label _status = new();
    private readonly Label[] _idCells = new Label[4];
    private readonly Label _instruction = new();
    private readonly HashSet<int> _completedIds = [];
    private int _expectedId = 1;
    private int? _lastId;
    private int _stableReads;
    private readonly InspectionAuditService _audit = new();
    private int _attempt;
    private bool _attemptCompleted;
    private Task? _monitorTask;
    private bool _closing;
    private bool _closeCleanupRunning;

    public DioIdSwitchInspectionForm(IDioIdSwitchReader reader, InspectionSession session)
    {
        _reader = reader;
        _session = session;
        _session.Results.InvalidateFinalization();
        _session.Results.DipSwitchPassed = null;
        _session.Results.DipStatus = InspectionExecutionStatus.Running;
        _attempt = _audit.BeginAttempt(_session, "dip_switch", "ID 1~4 순차 위치 검사");
        new InspectionCheckpointService().TrySave(_session);
        Text = "DIO ID DIP 스위치 검사";
        StartPosition = FormStartPosition.CenterParent;
        ResponsiveWindow.Configure(this, new Size(920, 590), new Size(760, 540), scrollWhenCompact: true);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Colors.Surface };
        header.Controls.Add(new Label { Location = new Point(30, 17), Size = new Size(520, 34), Font = Fonts.PageTitle, ForeColor = Colors.TextPrimary, Text = "DIO ID DIP 스위치 검사" });
        header.Controls.Add(new Label { Location = new Point(32, 54), Size = new Size(520, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "화면 안내에 따라 ID 1부터 4까지 순서대로 설정합니다." });
        var reset = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Refresh, Text = "RESET", Location = new Point(740, 24), Size = new Size(150, 44) };
        var fail = new AppButton { Variant = AppButtonVariant.Danger, IconGlyph = AppIcon.Warning, Text = "FAIL 판정", Location = new Point(576, 24), Size = new Size(150, 44) };
        fail.Click += (_, _) => MarkFailed();
        header.Controls.Add(fail);
        reset.Click += (_, _) => ResetProgress();
        header.Controls.Add(reset);
        fail.BringToFront();
        reset.BringToFront();
        Controls.Add(header);

        var idCard = new AppCard { Location = new Point(30, 116), Size = new Size(270, 100), CardLabel = "현재 ID", CardValue = "--", Icon = AppIcon.Settings, IconColor = Colors.Warning, IconSurfaceColor = Colors.WarningSurface, ValueColor = Colors.Warning };
        var pin1Card = new AppCard { Location = new Point(316, 116), Size = new Size(270, 100), CardLabel = "ID_SET1 · PA0", CardValue = "--", Icon = AppIcon.Pulse, IconColor = Colors.Primary, IconSurfaceColor = Colors.SurfaceBlue, ValueColor = Colors.TextMuted };
        var pin2Card = new AppCard { Location = new Point(602, 116), Size = new Size(270, 100), CardLabel = "ID_SET2 · PC15", CardValue = "--", Icon = AppIcon.Pulse, IconColor = Colors.Primary, IconSurfaceColor = Colors.SurfaceBlue, ValueColor = Colors.TextMuted };
        Controls.Add(idCard); Controls.Add(pin1Card); Controls.Add(pin2Card);

        var main = new AppPanel { Location = new Point(30, 240), Size = new Size(560, 300) };
        main.Controls.Add(new Label { Location = new Point(24, 18), Size = new Size(300, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "실시간 스위치 상태" });
        _id.Location = new Point(24, 54); _id.Size = new Size(512, 88); _id.Font = new Font("맑은 고딕", 40, FontStyle.Bold); _id.ForeColor = Colors.Warning; _id.TextAlign = ContentAlignment.MiddleCenter; _id.Text = "ID --";
        _pins.Location = new Point(24, 145); _pins.Size = new Size(512, 28); _pins.Font = Fonts.ButtonText; _pins.ForeColor = Colors.TextSecondary; _pins.TextAlign = ContentAlignment.MiddleCenter; _pins.Text = "ID_SET1: --   ID_SET2: --";
        main.Controls.Add(_id); main.Controls.Add(_pins);
        var grid = new TableLayoutPanel { Location = new Point(24, 190), Size = new Size(512, 64), ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent };
        for (var i = 0; i < 4; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var label = new Label { Dock = DockStyle.Fill, Margin = new Padding(5), BackColor = Colors.SurfaceBlue, ForeColor = Colors.TextMuted, Font = Fonts.ButtonText, TextAlign = ContentAlignment.MiddleCenter, Text = $"ID {i + 1}" };
            _idCells[i] = label; grid.Controls.Add(label, i, 0);
        }
        _idCells[0].BackColor = Colors.WarningSurface;
        _idCells[0].ForeColor = Colors.Warning;
        main.Controls.Add(grid);
        _status.Location = new Point(24, 263); _status.Size = new Size(512, 24); _status.Font = Fonts.Caption; _status.ForeColor = Colors.TextMuted; _status.TextAlign = ContentAlignment.MiddleCenter; _status.Text = "ST-LINK 연결 확인 중...";
        main.Controls.Add(_status); Controls.Add(main);

        var guide = new AppPanel { Location = new Point(614, 240), Size = new Size(258, 300) };
        guide.Controls.Add(new Label { Location = new Point(22, 18), Size = new Size(210, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "단계별 안내" });
        guide.Controls.Add(new Label { Location = new Point(22, 52), Size = new Size(210, 102), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "ID 1: LOW / LOW\nID 2: HIGH / LOW\nID 3: LOW / HIGH\nID 4: HIGH / HIGH\n\n각 위치는 두 번 연속 확인합니다." });
        _instruction.SetBounds(22, 168, 210, 76);
        _instruction.Font = Fonts.ButtonText;
        _instruction.ForeColor = Colors.Warning;
        _instruction.BackColor = Colors.WarningSurface;
        _instruction.TextAlign = ContentAlignment.MiddleCenter;
        _instruction.Text = "DIP 스위치를 ID 1\n(LOW / LOW)로 설정하세요.";
        guide.Controls.Add(_instruction);
        guide.Controls.Add(new Label { Location = new Point(22, 252), Size = new Size(210, 28), Font = Fonts.Caption, ForeColor = Colors.TextMuted, TextAlign = ContentAlignment.MiddleCenter, Text = "완료 시 자동 PASS 판정" });
        Controls.Add(guide);

        Shown += (_, _) => _monitorTask = MonitorAsync(idCard, pin1Card, pin2Card);
        FormClosing += OnClosing;
    }

    private async Task MonitorAsync(AppCard idCard, AppCard pin1Card, AppCard pin2Card)
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var state = await _reader.ReadAsync(_cts.Token);
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    if (_session.Results.DipStatus == InspectionExecutionStatus.Error)
                        _session.Results.DipStatus = InspectionExecutionStatus.Running;
                    _id.Text = $"ID {state.Id}"; idCard.CardValue = $"ID {state.Id}";
                    var pin1 = state.IdSet1High ? "HIGH" : "LOW"; var pin2 = state.IdSet2High ? "HIGH" : "LOW";
                    _pins.Text = $"ID_SET1: {pin1}   ID_SET2: {pin2}"; pin1Card.CardValue = pin1; pin2Card.CardValue = pin2;
                    pin1Card.ValueColor = pin2Card.ValueColor = Colors.Primary;
                    AdvanceIfStable(state.Id);
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _session.Results.DipStatus = InspectionExecutionStatus.Error;
                if (IsDisposed) return;
                BeginInvoke(() => { _id.Text = "ID --"; idCard.CardValue = "--"; pin1Card.CardValue = pin2Card.CardValue = "--"; _pins.Text = "ID_SET1: --   ID_SET2: --"; _status.Text = ex.Message; _status.ForeColor = Colors.Danger; });
            }
            try { await Task.Delay(700, _cts.Token); } catch (OperationCanceledException) { return; }
        }
    }

    private void AdvanceIfStable(int id)
    {
        if (_session.Results.DipStatus == InspectionExecutionStatus.Fail) return;
        if (_expectedId > 4)
        {
            _status.Text = "ID 1~4 전 위치 확인 완료 · PASS";
            _status.ForeColor = Colors.Success;
            return;
        }

        if (id != _expectedId)
        {
            _lastId = id;
            _stableReads = 0;
            _status.Text = $"현재 ID {id} 감지 · ID {_expectedId}로 설정하세요.";
            _status.ForeColor = Colors.Warning;
            return;
        }

        _stableReads = _lastId == id ? _stableReads + 1 : 1;
        _lastId = id;
        _status.Text = $"ID {id} 안정 확인 중 ({_stableReads}/2)";
        _status.ForeColor = Colors.Primary;
        if (_stableReads < 2) return;

        _completedIds.Add(id);
        _idCells[id - 1].BackColor = Colors.SuccessSurface;
        _idCells[id - 1].ForeColor = Colors.Success;
        _expectedId++;
        _stableReads = 0;
        _lastId = null;
        new InspectionCheckpointService().TrySave(_session);

        if (_expectedId > 4)
        {
            _session.Results.DipSwitchPassed = true;
            _session.Results.DipStatus = InspectionExecutionStatus.Pass;
            CompleteAttempt();
            _instruction.Text = "ID 1~4 확인 완료\n검사 결과 PASS";
            _instruction.ForeColor = Colors.Success;
            _instruction.BackColor = Colors.SuccessSurface;
            _status.Text = "ID 1~4 전 위치 확인 완료 · PASS";
            _status.ForeColor = Colors.Success;
            return;
        }

        _idCells[_expectedId - 1].BackColor = Colors.WarningSurface;
        _idCells[_expectedId - 1].ForeColor = Colors.Warning;
        _instruction.Text = $"다음: DIP 스위치를 ID {_expectedId}\n({Combination(_expectedId)})로 설정하세요.";
        _status.Text = $"ID {id} 확인 완료 · 다음 ID {_expectedId}";
        _status.ForeColor = Colors.Success;
    }

    private void ResetProgress()
    {
        CompleteAttempt();
        _session.Results.InvalidateFinalization();
        _completedIds.Clear();
        _expectedId = 1;
        _lastId = null;
        _stableReads = 0;
        _session.Results.DipSwitchPassed = null;
        _session.Results.DipStatus = InspectionExecutionStatus.Running;
        _attempt = _audit.BeginAttempt(_session, "dip_switch", "검사 진행 상태 초기화 후 재검사");
        _attemptCompleted = false;
        new InspectionCheckpointService().TrySave(_session);
        foreach (var cell in _idCells) { cell.BackColor = Colors.SurfaceBlue; cell.ForeColor = Colors.TextMuted; }
        _idCells[0].BackColor = Colors.WarningSurface;
        _idCells[0].ForeColor = Colors.Warning;
        _instruction.Text = "DIP 스위치를 ID 1\n(LOW / LOW)로 설정하세요.";
        _instruction.ForeColor = Colors.Warning;
        _instruction.BackColor = Colors.WarningSurface;
        _status.Text = "검사 진행 상태를 초기화했습니다.";
        _status.ForeColor = Colors.TextMuted;
    }

    private static string Combination(int id) => id switch
    {
        1 => "LOW / LOW",
        2 => "HIGH / LOW",
        3 => "LOW / HIGH",
        _ => "HIGH / HIGH"
    };

    private void MarkFailed()
    {
        if (MessageBox.Show(this, "ID DIP 스위치 검사를 FAIL로 판정하시겠습니까?", "FAIL 판정 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _session.Results.InvalidateFinalization();
        _session.Results.DipSwitchPassed = false;
        _session.Results.DipStatus = InspectionExecutionStatus.Fail;
        CompleteAttempt();
        _instruction.Text = "검사자 FAIL 판정";
        _instruction.ForeColor = Colors.Danger;
        _instruction.BackColor = Colors.DangerSurface;
        _status.Text = "ID DIP 스위치 검사 · FAIL";
        _status.ForeColor = Colors.Danger;
        new InspectionCheckpointService().TrySave(_session);
    }

    private void CompleteAttempt()
    {
        if (_attemptCompleted || _attempt == 0) return;
        _audit.CompleteAttempt(_session, "dip_switch", _attempt, _session.Results.DipStatus, _status.Text);
        _attemptCompleted = true;
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (_closeCleanupRunning) return;
        _closeCleanupRunning = true;
        if (_session.Results.DipStatus == InspectionExecutionStatus.Running)
            _session.Results.DipStatus = InspectionExecutionStatus.Stopped;
        CompleteAttempt();
        _cts.Cancel();
        try
        {
            if (_monitorTask is not null)
                await SafetyOperationService.RunWithTimeoutAsync(
                    "DIP ST-LINK 감시 종료",
                    SafetyOperationService.DioOutputOffTimeout,
                    ct => _monitorTask.WaitAsync(ct));
        }
        catch (Exception ex)
        {
            _session.Results.DipStatus = InspectionExecutionStatus.Error;
            new InspectionAuditService().TryRecord(_session, "safety_cleanup_failed", "dip_switch", "error", ex.Message, ex);
            MessageBox.Show(this, $"ST-LINK 읽기 작업을 종료하지 못했습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "검사 종료 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        new InspectionCheckpointService().TrySave(_session);
        _closing = true;
        Close();
    }

    protected override void Dispose(bool disposing) { if (disposing) _cts.Dispose(); base.Dispose(disposing); }
}
