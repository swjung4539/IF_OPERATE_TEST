using OperateTest.App.Services;
using OperateTest.App.Services.StLink;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class HomeForm : Form
{
    private readonly ISerialConnectionManager _connections;
    private readonly IInspectionProtocolServices _protocols;
    private readonly IDioIdSwitchReader _dioIdReader;
    private readonly ICommRotaryReader _rotaryReader;

    public HomeForm(ISerialConnectionManager connections, IInspectionProtocolServices protocols, IDioIdSwitchReader dioIdReader, ICommRotaryReader rotaryReader)
    {
        _connections = connections; _protocols = protocols; _dioIdReader = dioIdReader; _rotaryReader = rotaryReader;
        Text = "Operate Test";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1600, 900);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;
        Controls.Add(new AppTitleBar { ProductTitle = "Operate Test" });

        Controls.Add(new Label { Location = new Point(0, 122), Size = new Size(1600, 58), Font = new Font("맑은 고딕", 30, FontStyle.Bold), ForeColor = Colors.TextPrimary, TextAlign = ContentAlignment.MiddleCenter, Text = "검사 대상을 선택하세요" });
        Controls.Add(new Label { Location = new Point(0, 184), Size = new Size(1600, 28), Font = Fonts.Body, ForeColor = Colors.Primary, TextAlign = ContentAlignment.MiddleCenter, Text = "수입검사를 진행할 보드를 선택합니다." });

        var comm = BuildCard(150, Colors.Primary, Colors.SurfaceBlue, AppIcon.Network, "COMM 보드 검사",
            "MAIN 통신 경로와 로터리 스위치,\nFAULT 접점을 검사합니다.",
            [(AppIcon.Network, "5개 통신 경로"), (AppIcon.Check, "통신 반복 검사"), (AppIcon.Settings, "로터리 0~F"), (AppIcon.Shield, "TB18 FAULT")],
            "COMM 보드 검사 선택");
        var dio = BuildCard(826, Colors.Warning, Colors.WarningSurface, AppIcon.Server, "DIO 보드 검사",
            "DO·DI 루프백과 ID DIP 스위치\n상태를 검사합니다.",
            [(AppIcon.Keyboard, "RS-485 연결"), (AppIcon.Swap, "16채널 루프백"), (AppIcon.Settings, "ID 1~4"), (AppIcon.Shield, "종료 시 OFF")],
            "DIO 보드 검사 선택");
        comm.Button.Click += (_, _) => OpenChild(new CommInspectionForm(_connections, _protocols, _rotaryReader));
        dio.Button.Click += (_, _) => OpenChild(new DioInspectionForm(_connections, _protocols, _dioIdReader));

        Controls.Add(new Label { Location = new Point(0, 744), Size = new Size(1600, 28), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, TextAlign = ContentAlignment.MiddleCenter, Text = "ⓘ  검사 대상을 선택하면 해당 보드의 대시보드가 열립니다." });
    }

    private (AppPanel Card, AppButton Button) BuildCard(int x, Color accent, Color accentSurface, AppIcon icon, string title, string description, (AppIcon Icon, string Label)[] tiles, string buttonText)
    {
        var card = new AppPanel { Location = new Point(x, 260), Size = new Size(624, 420), BorderColor = accent, CornerRadius = 14 };
        var circle = new IconCircle { Icon = icon, Accent = accent, Surface = accentSurface, Location = new Point(24, 24), Size = new Size(90, 90) };
        card.Controls.Add(circle);
        card.Controls.Add(new Label { Location = new Point(136, 30), Size = new Size(430, 34), Font = new Font("맑은 고딕", 18, FontStyle.Bold), ForeColor = accent, Text = title });
        card.Controls.Add(new Label { Location = new Point(136, 69), Size = new Size(430, 52), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = description });
        card.Controls.Add(new Panel { Location = new Point(24, 150), Size = new Size(576, 1), BackColor = Colors.Divider });

        var tileWidth = 135;
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = new FeatureTile { Icon = tiles[i].Icon, Accent = accent, Label = tiles[i].Label, Location = new Point(24 + i * 147, 174), Size = new Size(tileWidth, 130) };
            card.Controls.Add(tile);
        }

        var button = new AppButton { Variant = accent == Colors.Warning ? AppButtonVariant.OutlinePrimary : AppButtonVariant.Primary, IconGlyph = AppIcon.ChevronRight, Text = buttonText, Location = new Point(24, 332), Size = new Size(576, 56), Font = new Font("맑은 고딕", 12, FontStyle.Bold) };
        card.Controls.Add(button); Controls.Add(card); return (card, button);
    }

    private void OpenChild(Form form) { using (form) form.ShowDialog(this); }

    private sealed class IconCircle : Control
    {
        public AppIcon Icon { get; init; }
        public Color Accent { get; init; } = Colors.Primary;
        public Color Surface { get; init; } = Colors.SurfaceBlue;
        public IconCircle() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            GraphicsHelper.SetHighQuality(e.Graphics); using var brush = new SolidBrush(Surface); e.Graphics.FillEllipse(brush, ClientRectangle);
            IconPainter.Draw(e.Graphics, Icon, Rectangle.Inflate(ClientRectangle, -23, -23), Accent, 2.1f);
        }
    }

    private sealed class FeatureTile : Control
    {
        public AppIcon Icon { get; init; }
        public Color Accent { get; init; } = Colors.Primary;
        public string Label { get; init; } = string.Empty;
        public FeatureTile() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            GraphicsHelper.SetHighQuality(e.Graphics);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = GraphicsHelper.RoundedRect(rect, 10); using var bg = new SolidBrush(Colors.Surface); using var pen = new Pen(Colors.Border, 1);
            e.Graphics.FillPath(bg, path); e.Graphics.DrawPath(pen, path);
            IconPainter.Draw(e.Graphics, Icon, new Rectangle(Width / 2 - 18, 20, 36, 36), Accent, 2);
            TextRenderer.DrawText(e.Graphics, Label, Fonts.Caption, new Rectangle(8, 72, Width - 16, 42), Colors.TextPrimary, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }
}
