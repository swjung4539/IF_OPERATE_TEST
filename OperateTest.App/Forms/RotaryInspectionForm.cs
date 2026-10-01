using OperateTest.App.Services.StLink;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class RotaryInspectionForm : Form
{
    private readonly ICommRotaryReader _reader;
    private readonly CancellationTokenSource _cts = new();
    private readonly Label[] _positions = new Label[16];
    private readonly HashSet<int> _seen = [];
    private readonly Label _value = new();
    private readonly Label _raw = new();
    private readonly Label _status = new();
    private readonly AppCard _seenCard = new();

    public RotaryInspectionForm(ICommRotaryReader reader)
    {
        _reader = reader;
        Text = "로터리 스위치 검사";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 680);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Colors.Surface };
        header.Controls.Add(new Label { Location = new Point(30, 17), Size = new Size(560, 34), Font = Fonts.PageTitle, ForeColor = Colors.TextPrimary, Text = "COMM 로터리 스위치 검사" });
        header.Controls.Add(new Label { Location = new Point(32, 54), Size = new Size(700, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "ST-LINK로 PC0~PC3을 읽어 물리 스위치 위치를 실시간 표시합니다." });
        var reset = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Refresh, Text = "RESET", Location = new Point(884, 24), Size = new Size(150, 44) };
        reset.Click += (_, _) => ResetHistory(); header.Controls.Add(reset); Controls.Add(header);

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
        main.Controls.Add(grid);
        _status.Location = new Point(24, 333); _status.Size = new Size(612, 34); _status.Font = Fonts.ButtonText; _status.ForeColor = Colors.TextMuted; _status.TextAlign = ContentAlignment.MiddleCenter; _status.Text = "ST-LINK 연결 확인 중...";
        main.Controls.Add(_status); Controls.Add(main);

        var guide = new AppPanel { Location = new Point(714, 240), Size = new Size(318, 390) };
        guide.Controls.Add(new Label { Location = new Point(22, 18), Size = new Size(250, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "검사 방법" });
        guide.Controls.Add(new Label { Location = new Point(22, 64), Size = new Size(272, 210), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = "1. COMM 보드에 전원을 공급합니다.\n\n2. ST-LINK를 연결합니다.\n\n3. 로터리 스위치를 0부터 F까지 돌립니다.\n\n4. 지나간 위치가 초록색으로 표시되는지 확인합니다." });
        guide.Controls.Add(new Label { Location = new Point(22, 300), Size = new Size(272, 58), Font = Fonts.ButtonText, ForeColor = Colors.Primary, BackColor = Colors.SurfaceBlue, TextAlign = ContentAlignment.MiddleCenter, Text = "16개 위치가 모두 초록색이면\n검사 완료입니다." });
        Controls.Add(guide);

        Shown += (_, _) => _ = MonitorAsync(currentCard, linkCard);
        FormClosing += (_, _) => _cts.Cancel();
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
                    _value.Text = state.HexPosition; currentCard.CardValue = state.HexPosition;
                    _raw.Text = $"PC3 PC2 PC1 PC0: {Convert.ToString(state.RawHighNibble, 2).PadLeft(4, '0')} (접점 LOW 반전)";
                    _seen.Add(state.Position); _seenCard.CardValue = $"{_seen.Count} / 16";
                    _positions[state.Position].BackColor = Colors.SuccessSurface; _positions[state.Position].ForeColor = Colors.Success;
                    _status.Text = _seen.Count == 16 ? "0~F 전 위치 확인 완료" : $"ST-LINK 연결됨 · 확인 {_seen.Count}/16";
                    _status.ForeColor = _seen.Count == 16 ? Colors.Success : Colors.Primary;
                    linkCard.CardValue = "연결됨"; linkCard.ValueColor = Colors.Success;
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                BeginInvoke(() => { _value.Text = "--"; currentCard.CardValue = "--"; linkCard.CardValue = "연결 실패"; linkCard.ValueColor = Colors.Danger; _status.Text = ex.Message; _status.ForeColor = Colors.Danger; });
            }
            try { await Task.Delay(500, _cts.Token); } catch (OperationCanceledException) { return; }
        }
    }

    private void ResetHistory()
    {
        _seen.Clear(); _seenCard.CardValue = "0 / 16";
        foreach (var label in _positions) { label.BackColor = Colors.SurfaceBlue; label.ForeColor = Colors.TextMuted; }
        _status.Text = "확인 이력을 초기화했습니다."; _status.ForeColor = Colors.TextMuted;
    }

    protected override void Dispose(bool disposing) { if (disposing) _cts.Dispose(); base.Dispose(disposing); }
}
