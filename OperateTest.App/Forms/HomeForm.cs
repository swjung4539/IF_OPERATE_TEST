using OperateTest.App.Models;
using OperateTest.App.Services;
using OperateTest.App.Services.StLink;
using TestEmulator.Controls;
using TestEmulator.Layout;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class HomeForm : Form
{
    private readonly ISerialConnectionManager _connections;
    private readonly IInspectionProtocolServices _protocols;
    private readonly IDioIdSwitchReader _dioIdReader;
    private readonly ICommRotaryReader _rotaryReader;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Label _footerLabel;
    private readonly AppPanel _commCard;
    private readonly AppPanel _dioCard;

    public HomeForm(ISerialConnectionManager connections, IInspectionProtocolServices protocols, IDioIdSwitchReader dioIdReader, ICommRotaryReader rotaryReader)
    {
        _connections = connections; _protocols = protocols; _dioIdReader = dioIdReader; _rotaryReader = rotaryReader;
        Text = "Operate Test";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ResponsiveWindow.Configure(this, new Size(1480, 820), new Size(980, 650));
        WindowState = FormWindowState.Normal;
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;
        Controls.Add(new AppTitleBar { ProductTitle = "Operate Test" });

        _titleLabel = new Label { Location = new Point(0, 122), Size = new Size(1600, 58), Font = new Font("맑은 고딕", 30, FontStyle.Bold), ForeColor = Colors.TextPrimary, TextAlign = ContentAlignment.MiddleCenter, Text = "검사 대상을 선택하세요" };
        _subtitleLabel = new Label { Location = new Point(0, 184), Size = new Size(1600, 28), Font = Fonts.Body, ForeColor = Colors.Primary, TextAlign = ContentAlignment.MiddleCenter, Text = "수입검사를 진행할 보드를 선택합니다." };
        Controls.Add(_titleLabel);
        Controls.Add(_subtitleLabel);

        var comm = BuildCard(150, Colors.Primary, Colors.SurfaceBlue, AppIcon.Network, "COMM 보드 검사",
            "MAIN 통신 경로와 로터리 스위치,\nFAULT 접점을 검사합니다.",
            [(AppIcon.Network, "4개 통신 경로"), (AppIcon.Check, "통신 반복 검사"), (AppIcon.Settings, "로터리 0~F"), (AppIcon.Shield, "TB18 FAULT")],
            "COMM 보드 검사 선택");
        var dio = BuildCard(826, Colors.Warning, Colors.WarningSurface, AppIcon.Server, "DIO 보드 검사",
            "DO·DI 루프백과 ID DIP 스위치\n상태를 검사합니다.",
            [(AppIcon.Keyboard, "RS-485 연결"), (AppIcon.Swap, "16채널 루프백"), (AppIcon.Settings, "ID 0~3"), (AppIcon.Shield, "종료 시 OFF")],
            "DIO 보드 검사 선택");
        _commCard = comm.Card;
        _dioCard = dio.Card;
        comm.Button.Click += (_, _) => BeginInspection(InspectionBoardType.Communication);
        dio.Button.Click += (_, _) => BeginInspection(InspectionBoardType.DigitalIo);

        _footerLabel = new Label { Location = new Point(0, 744), Size = new Size(1600, 28), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, TextAlign = ContentAlignment.MiddleCenter, Text = $"ⓘ  검사 대상을 선택하면 해당 보드의 대시보드가 열립니다.  ·  v{ApplicationBuildInfo.Version}" };
        Controls.Add(_footerLabel);
        var diagnostics = new AppButton
        {
            Variant = AppButtonVariant.Secondary,
            IconGlyph = AppIcon.Info,
            Text = "버전·진단 정보",
            Size = new Size(164, 38),
            Location = new Point(ClientSize.Width - 184, 72),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        diagnostics.Click += (_, _) => ShowDiagnostics();
        Controls.Add(diagnostics);
        Resize += (_, _) => LayoutHome();
        LayoutHome();
    }

    private void ShowDiagnostics()
    {
        var buildTime = ApplicationBuildInfo.BuildFileTime == default
            ? "확인할 수 없음"
            : ApplicationBuildInfo.BuildFileTime.ToString("yyyy-MM-dd HH:mm:ss");
        MessageBox.Show(this,
            $"프로그램 버전: {ApplicationBuildInfo.Version}{Environment.NewLine}" +
            $"배포 파일 시각: {buildTime}{Environment.NewLine}" +
            $"실행 파일: {ApplicationBuildInfo.ExecutablePath}{Environment.NewLine}" +
            $"SHA-256: {ApplicationBuildInfo.ExecutableSha256}{Environment.NewLine}{Environment.NewLine}" +
            "검사 감사 로그는 선택한 결과 Excel 폴더의 .inspection-logs 폴더에 저장됩니다.",
            "버전 및 진단 정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private (AppPanel Card, AppButton Button) BuildCard(int x, Color accent, Color accentSurface, AppIcon icon, string title, string description, (AppIcon Icon, string Label)[] tiles, string buttonText)
    {
        var card = new AppPanel { Location = new Point(x, 260), Size = new Size(624, 420), BorderColor = accent, CornerRadius = 14 };
        var circle = new IconCircle { Icon = icon, Accent = accent, Surface = accentSurface, Location = new Point(24, 24), Size = new Size(90, 90) };
        card.Controls.Add(circle);
        card.Controls.Add(new Label { Location = new Point(136, 25), Size = new Size(430, 46), Font = new Font("맑은 고딕", 18, FontStyle.Bold), ForeColor = accent, Text = title });
        card.Controls.Add(new Label { Location = new Point(136, 72), Size = new Size(430, 64), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = description });
        card.Controls.Add(new Panel { Location = new Point(24, 150), Size = new Size(576, 1), BackColor = Colors.Divider });

        var tileWidth = 135;
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = new FeatureTile { Icon = tiles[i].Icon, Accent = accent, Label = tiles[i].Label, Location = new Point(24 + i * 147, 174), Size = new Size(tileWidth, 130) };
            card.Controls.Add(tile);
        }

        var button = new AppButton { Variant = accent == Colors.Warning ? AppButtonVariant.Warning : AppButtonVariant.Primary, IconGlyph = AppIcon.ChevronRight, Text = buttonText, Location = new Point(24, 332), Size = new Size(576, 56), Font = new Font("맑은 고딕", 12, FontStyle.Bold) };
        card.Controls.Add(button); Controls.Add(card); return (card, button);
    }

    private void LayoutHome()
    {
        var wideLayout = ClientSize.Width >= 1340;
        var cardWidth = wideLayout ? 624 : (ClientSize.Width - 60) / 2;
        var cardGap = wideLayout ? 52 : 20;
        var cardsWidth = cardWidth * 2 + cardGap;
        var left = Math.Max(20, (ClientSize.Width - cardsWidth) / 2);
        var titleY = Math.Max(76, 112 + (ClientSize.Height - 820) / 4);
        var cardsY = titleY + 112;

        LayoutChoiceCard(_commCard, cardWidth);
        LayoutChoiceCard(_dioCard, cardWidth);
        _titleLabel.SetBounds(0, titleY, ClientSize.Width, 58);
        _subtitleLabel.SetBounds(0, titleY + 62, ClientSize.Width, 28);
        _commCard.Location = new Point(left, cardsY);
        _dioCard.Location = new Point(left + cardWidth + cardGap, cardsY);
        _footerLabel.SetBounds(0, Math.Max(cardsY + 424, ClientSize.Height - 42), ClientSize.Width, 28);
    }

    private static void LayoutChoiceCard(AppPanel card, int width)
    {
        card.Width = width;
        var labels = card.Controls.OfType<Label>().OrderBy(label => label.Top).ToArray();
        if (labels.Length >= 2)
        {
            labels[0].Width = Math.Max(180, width - 170);
            labels[1].Width = Math.Max(180, width - 170);
        }

        var divider = card.Controls.OfType<Panel>().FirstOrDefault();
        if (divider != null) divider.Width = width - 48;

        var tiles = card.Controls.OfType<FeatureTile>().OrderBy(tile => tile.Left).ToArray();
        const int tileGap = 12;
        var tileWidth = Math.Max(80, (width - 48 - tileGap * 3) / 4);
        for (var i = 0; i < tiles.Length; i++)
            tiles[i].SetBounds(24 + i * (tileWidth + tileGap), 174, tileWidth, 130);

        var button = card.Controls.OfType<AppButton>().FirstOrDefault();
        if (button != null) button.Width = width - 48;
    }

    private void BeginInspection(InspectionBoardType boardType)
    {
        InspectionSession? session = null;
        var forceNewResultFile = false;
        Guid? previousSessionId = null;

        while (true)
        {
            if (session is null)
            {
                using var setup = new InspectionSetupForm(boardType, forceNewResultFile, previousSessionId);
                if (setup.ShowDialog(this) != DialogResult.OK || setup.Session is null) return;
                session = setup.Session;
                forceNewResultFile = false;
                previousSessionId = null;
            }

            using InspectionShellForm inspection = boardType == InspectionBoardType.Communication
                ? new CommInspectionForm(_connections, _protocols, _rotaryReader, session)
                : new DioInspectionForm(_connections, _protocols, _dioIdReader, session);
            inspection.ShowDialog(this);

            switch (inspection.CompletionAction)
            {
                case InspectionContinuationAction.SameWorkbook:
                    if (!TryPrepareNextBoard(session, out var nextSession)) return;
                    session = nextSession;
                    break;

                case InspectionContinuationAction.NewWorkbook:
                    previousSessionId = session.SessionId;
                    session = null;
                    forceNewResultFile = true;
                    break;

                default:
                    return;
            }
        }
    }

    private bool TryPrepareNextBoard(InspectionSession completedSession, out InspectionSession? nextSession)
    {
        nextSession = null;
        try
        {
            var usage = new InspectionWorkbookService().GetUsage(completedSession.ResultPath, completedSession.BoardType);
            if (usage.IsFull)
            {
                MessageBox.Show(this,
                    "현재 결과 문서의 입력 가능한 30개 행을 모두 사용했습니다. 새 문서로 검사를 시작하세요.",
                    "결과 문서 사용 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            using var dialog = new NextBoardSetupForm(completedSession, usage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Session is null) return false;
            nextSession = dialog.Session;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"다음 보드 검사를 준비하지 못했습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "다음 보드 준비 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

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
