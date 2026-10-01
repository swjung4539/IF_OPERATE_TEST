using OperateTest.App.Services.StLink;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class DioIdSwitchInspectionForm : Form
{
    private readonly IDioIdSwitchReader _reader;
    private readonly CancellationTokenSource _cts = new();
    private readonly Label _id = new();
    private readonly Label _pins = new();
    private readonly Label _status = new();
    private readonly Label[] _idCells = new Label[4];

    public DioIdSwitchInspectionForm(IDioIdSwitchReader reader)
    {
        _reader = reader;
        Text = "DIO ID DIP 스위치 검사";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 590);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Colors.Surface };
        header.Controls.Add(new Label { Location = new Point(30, 17), Size = new Size(560, 34), Font = Fonts.PageTitle, ForeColor = Colors.TextPrimary, Text = "DIO ID DIP 스위치 검사" });
        header.Controls.Add(new Label { Location = new Point(32, 54), Size = new Size(750, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "ID_SET1·ID_SET2 GPIO를 읽어 물리 DIP 위치에 따른 ID 1~4를 실시간 표시합니다." });
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
        main.Controls.Add(grid);
        _status.Location = new Point(24, 263); _status.Size = new Size(512, 24); _status.Font = Fonts.Caption; _status.ForeColor = Colors.TextMuted; _status.TextAlign = ContentAlignment.MiddleCenter; _status.Text = "ST-LINK 연결 확인 중...";
        main.Controls.Add(_status); Controls.Add(main);

        var guide = new AppPanel { Location = new Point(614, 240), Size = new Size(258, 300) };
        guide.Controls.Add(new Label { Location = new Point(22, 18), Size = new Size(210, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "ID 조합" });
        guide.Controls.Add(new Label { Location = new Point(22, 62), Size = new Size(210, 170), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = "LOW / LOW   = ID 1\n\nHIGH / LOW  = ID 2\n\nLOW / HIGH  = ID 3\n\nHIGH / HIGH = ID 4" });
        guide.Controls.Add(new Label { Location = new Point(22, 242), Size = new Size(210, 34), Font = Fonts.Caption, ForeColor = Colors.Warning, BackColor = Colors.WarningSurface, TextAlign = ContentAlignment.MiddleCenter, Text = "물리 DIP 상태 기준" });
        Controls.Add(guide);

        Shown += (_, _) => _ = MonitorAsync(idCard, pin1Card, pin2Card);
        FormClosing += (_, _) => _cts.Cancel();
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
                    _id.Text = $"ID {state.Id}"; idCard.CardValue = $"ID {state.Id}";
                    var pin1 = state.IdSet1High ? "HIGH" : "LOW"; var pin2 = state.IdSet2High ? "HIGH" : "LOW";
                    _pins.Text = $"ID_SET1: {pin1}   ID_SET2: {pin2}"; pin1Card.CardValue = pin1; pin2Card.CardValue = pin2;
                    pin1Card.ValueColor = pin2Card.ValueColor = Colors.Primary;
                    for (var i = 0; i < 4; i++) { _idCells[i].BackColor = i + 1 == state.Id ? Colors.WarningSurface : Colors.SurfaceBlue; _idCells[i].ForeColor = i + 1 == state.Id ? Colors.Warning : Colors.TextMuted; }
                    _status.Text = "ST-LINK 연결됨 · DIP 상태 실시간 확인"; _status.ForeColor = Colors.Success;
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                BeginInvoke(() => { _id.Text = "ID --"; idCard.CardValue = "--"; pin1Card.CardValue = pin2Card.CardValue = "--"; _pins.Text = "ID_SET1: --   ID_SET2: --"; _status.Text = ex.Message; _status.ForeColor = Colors.Danger; });
            }
            try { await Task.Delay(700, _cts.Token); } catch (OperationCanceledException) { return; }
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) _cts.Dispose(); base.Dispose(disposing); }
}
