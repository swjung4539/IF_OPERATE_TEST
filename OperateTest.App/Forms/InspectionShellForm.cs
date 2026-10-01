using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public abstract class InspectionShellForm : Form
{
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Colors.AppBackground };
    private readonly Label _pageTitle = new();
    private readonly Label _pageSubtitle = new();
    private readonly Dictionary<string, SideMenuButton> _menus = new();
    private readonly Label _connectionSummary = new();

    protected InspectionShellForm(string boardTitle, string boardSubtitle, Color accent, bool showSettings = true)
    {
        Text = boardTitle;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1280, 800);
        ClientSize = new Size(1600, 900);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        var top = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Colors.Surface };
        top.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Colors.Divider });
        top.Controls.Add(new IconBlock { Icon = AppIcon.Clipboard, Accent = accent, Location = new Point(18, 17), Size = new Size(38, 38) });
        top.Controls.Add(new Label { Location = new Point(70, 14), Size = new Size(520, 30), Font = Fonts.CardTitle, ForeColor = Colors.TextPrimary, Text = boardTitle });
        top.Controls.Add(new Label { Location = new Point(72, 43), Size = new Size(650, 20), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = boardSubtitle });
        _connectionSummary.Location = new Point(1040, 21);
        _connectionSummary.Size = new Size(500, 28);
        _connectionSummary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _connectionSummary.Font = Fonts.Caption;
        _connectionSummary.ForeColor = Colors.TextSecondary;
        _connectionSummary.TextAlign = ContentAlignment.MiddleRight;
        _connectionSummary.Text = "●  검사 준비 상태 확인";
        top.Controls.Add(_connectionSummary);

        var sidebar = new Panel { Dock = DockStyle.Left, Width = Metrics.SideMenuWidth, BackColor = Colors.Surface };
        sidebar.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Colors.Divider });
        var menuHost = new Panel { Dock = DockStyle.Top, Height = 288, Padding = new Padding(0, 8, 0, 0), BackColor = Colors.Surface };
        AddMenu(menuHost, "fault-switch", "FAULT 및 스위치 검사", AppIcon.Settings);
        AddMenu(menuHost, "test", "통신 검사", AppIcon.Check);
        if (showSettings) AddMenu(menuHost, "settings", "통신 설정", AppIcon.Network);
        AddMenu(menuHost, "dashboard", "대시보드", AppIcon.Home);
        sidebar.Controls.Add(menuHost);
        var back = new AppButton { Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.ChevronLeft, Text = "검사 대상 선택", Dock = DockStyle.Fill };
        back.Click += (_, _) => Close();
        var backWrap = new Panel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(16, 12, 16, 12), BackColor = Colors.Surface };
        backWrap.Controls.Add(back);
        sidebar.Controls.Add(backWrap);

        var pageHeader = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Colors.AppBackground };
        _pageTitle.Location = new Point(40, 24);
        _pageTitle.Size = new Size(900, 40);
        _pageTitle.Font = new Font("맑은 고딕", 22f, FontStyle.Bold);
        _pageTitle.ForeColor = Colors.TextPrimary;
        _pageSubtitle.Location = new Point(42, 69);
        _pageSubtitle.Size = new Size(1000, 25);
        _pageSubtitle.Font = Fonts.Body;
        _pageSubtitle.ForeColor = Colors.TextSecondary;
        pageHeader.Controls.Add(_pageTitle);
        pageHeader.Controls.Add(_pageSubtitle);

        var main = new Panel { Dock = DockStyle.Fill, BackColor = Colors.AppBackground };
        main.Controls.Add(_content);
        main.Controls.Add(pageHeader);
        Controls.Add(main);
        Controls.Add(sidebar);
        Controls.Add(top);
    }

    private void AddMenu(Panel host, string key, string text, AppIcon icon)
    {
        var button = new SideMenuButton { Text = text, Icon = icon, Tag = key, Dock = DockStyle.Top };
        button.Selected += (_, _) => Navigate(key);
        _menus[key] = button;
        host.Controls.Add(button);
    }

    protected void ShowPage(string key, string title, string subtitle, Control page)
    {
        foreach (var pair in _menus) pair.Value.IsSelected = pair.Key == key;
        _pageTitle.Text = title;
        _pageSubtitle.Text = subtitle;
        _content.SuspendLayout();
        _content.Controls.Clear();
        page.Dock = DockStyle.Fill;
        _content.Controls.Add(page);
        _content.ResumeLayout();
    }

    protected void SetTopStatus(string text, Color color)
    {
        _connectionSummary.Text = $"●  {text}";
        _connectionSummary.ForeColor = color;
    }

    protected abstract void Navigate(string key);

    private sealed class IconBlock : Control
    {
        public AppIcon Icon { get; init; }
        public Color Accent { get; init; } = Colors.Primary;
        public IconBlock() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            GraphicsHelper.SetHighQuality(e.Graphics);
            using var background = new SolidBrush(Accent);
            e.Graphics.FillRectangle(background, ClientRectangle);
            IconPainter.Draw(e.Graphics, Icon, new Rectangle(9, 9, Width - 18, Height - 18), Color.White, 1.7f);
        }
    }
}
