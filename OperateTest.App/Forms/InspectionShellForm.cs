using OperateTest.App.Models;
using OperateTest.App.Services;
using TestEmulator.Controls;
using TestEmulator.Layout;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public abstract class InspectionShellForm : Form
{
    private readonly Panel _content = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Colors.AppBackground };
    private readonly Label _pageTitle = new();
    private readonly Label _pageSubtitle = new();
    private readonly Dictionary<string, SideMenuButton> _menus = new();
    private readonly Label _connectionSummary = new();
    private readonly WorkflowProgressBar _workflow = new();
    private readonly Label _nextAction = new();
    private readonly Label _saveStatus = new();
    private readonly string _sessionPrefix;
    private readonly InspectionSession? _session;
    private readonly InspectionCheckpointService _checkpoints = new();
    private Func<(bool Ready, string Detail)> _connectionReadiness = () => (false, "연결 확인 필요");

    public InspectionContinuationAction CompletionAction { get; private set; }

    protected InspectionShellForm(string boardTitle, string boardSubtitle, Color accent, bool showSettings = true, InspectionSession? session = null)
    {
        _session = session;
        InspectionCheckpointService.SaveActivity += OnCheckpointSaveActivity;
        _sessionPrefix = session is null ? string.Empty : $"{session.SerialNumber} · {session.Inspector}";
        Text = boardTitle;
        StartPosition = FormStartPosition.CenterParent;
        ResponsiveWindow.Configure(this, new Size(1500, 860), new Size(1040, 680));
        WindowState = FormWindowState.Normal;
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        var top = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Colors.Surface };
        top.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Colors.Divider });
        top.Controls.Add(new IconBlock { Icon = AppIcon.Clipboard, Accent = accent, Location = new Point(18, 17), Size = new Size(38, 38) });
        top.Controls.Add(new Label { Location = new Point(70, 14), Size = new Size(520, 30), Font = Fonts.CardTitle, ForeColor = Colors.TextPrimary, Text = boardTitle });
        top.Controls.Add(new Label { Location = new Point(72, 43), Size = new Size(650, 20), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = boardSubtitle });
        _connectionSummary.Dock = DockStyle.Right;
        _connectionSummary.Width = 500;
        _connectionSummary.Padding = new Padding(0, 0, 24, 0);
        _connectionSummary.Font = Fonts.Caption;
        _connectionSummary.ForeColor = Colors.TextSecondary;
        _connectionSummary.TextAlign = ContentAlignment.MiddleRight;
        _connectionSummary.Text = string.IsNullOrEmpty(_sessionPrefix)
            ? "●  검사 준비 상태 확인"
            : $"{_sessionPrefix}  |  ●  검사 준비 상태 확인";
        top.Controls.Add(_connectionSummary);

        var sidebar = new Panel { Dock = DockStyle.Left, Width = Metrics.SideMenuWidth, BackColor = Colors.Surface };
        sidebar.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Colors.Divider });
        var menuHost = new Panel { Dock = DockStyle.Top, Height = 416, Padding = new Padding(0, 8, 0, 0), BackColor = Colors.Surface };
        var isDio = session?.BoardType == InspectionBoardType.DigitalIo;
        AddMenu(menuHost, "result", "최종 판정", AppIcon.Check);
        AddMenu(menuHost, "fault-switch", isDio ? "ID DIP 스위치 검사" : "FAULT 및 스위치 검사", AppIcon.Settings);
        AddMenu(menuHost, "test", isDio ? "DIO 입출력 검사" : "통신 검사", AppIcon.Check);
        if (showSettings) AddMenu(menuHost, "settings", "통신 설정", AppIcon.Network);
        AddMenu(menuHost, "manual", "육안 및 전원 검사", AppIcon.Clipboard);
        AddMenu(menuHost, "dashboard", "대시보드", AppIcon.Home);
        sidebar.Controls.Add(menuHost);
        _saveStatus.Location = new Point(16, 8);
        _saveStatus.Size = new Size(Metrics.SideMenuWidth - 32, 26);
        _saveStatus.Font = Fonts.Caption;
        _saveStatus.ForeColor = Colors.TextSecondary;
        _saveStatus.Text = session is null ? "저장 대상 없음" : "아직 자동 저장되지 않음";
        _saveStatus.TextAlign = ContentAlignment.MiddleLeft;
        _saveStatus.AutoEllipsis = true;
        var back = new AppButton { Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.ChevronLeft, Text = "검사 대상 선택", Location = new Point(16, 104), Size = new Size(Metrics.SideMenuWidth - 32, 50) };
        back.Click += (_, _) => Close();
        var saveCheckpoint = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Document, Text = "검사내용 임시 저장", Location = new Point(16, 42), Size = new Size(Metrics.SideMenuWidth - 32, 50), Enabled = session is not null };
        saveCheckpoint.Click += (_, _) =>
        {
            if (_session is null) return;
            try
            {
                var path = _checkpoints.Save(_session);
                MessageBox.Show(this,
                    $"현재 검사내용을 임시 저장했습니다.{Environment.NewLine}{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}최종 Excel 반영은 [최종 판정]에서 진행합니다.",
                    "임시 저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"검사내용을 임시 저장하지 못했습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}", "임시 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        var bottomActions = new Panel { Dock = DockStyle.Bottom, Height = 166, BackColor = Colors.Surface };
        bottomActions.Controls.Add(_saveStatus);
        bottomActions.Controls.Add(saveCheckpoint);
        bottomActions.Controls.Add(back);
        sidebar.Controls.Add(bottomActions);

        var pageHeader = new BufferedPanel { Dock = DockStyle.Top, Height = 206, BackColor = Colors.AppBackground };
        _pageTitle.Location = new Point(40, 16);
        _pageTitle.Size = new Size(900, 40);
        _pageTitle.Font = new Font("맑은 고딕", 22f, FontStyle.Bold);
        _pageTitle.ForeColor = Colors.TextPrimary;
        _pageSubtitle.Location = new Point(42, 57);
        _pageSubtitle.Size = new Size(1000, 25);
        _pageSubtitle.Font = Fonts.Body;
        _pageSubtitle.ForeColor = Colors.TextSecondary;
        _workflow.SetBounds(40, 88, 1110, 78);
        _nextAction.SetBounds(40, 174, 1110, 22);
        _nextAction.Font = Fonts.Caption;
        _nextAction.ForeColor = Colors.Primary;
        _nextAction.AutoEllipsis = true;
        pageHeader.Controls.Add(_pageTitle);
        pageHeader.Controls.Add(_pageSubtitle);
        pageHeader.Controls.Add(_workflow);
        pageHeader.Controls.Add(_nextAction);
        pageHeader.Resize += (_, _) => LayoutPageHeader(pageHeader);

        var main = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Colors.AppBackground };
        main.Controls.Add(_content);
        main.Controls.Add(pageHeader);
        Controls.Add(main);
        Controls.Add(sidebar);
        Controls.Add(top);
        LayoutPageHeader(pageHeader);
        Activated += (_, _) => RefreshWorkflow();
        FormClosing += (_, _) => { if (_session is not null) _checkpoints.TrySave(_session); };
    }

    private void LayoutPageHeader(Control pageHeader)
    {
        var contentWidth = Math.Max(_workflow.MinimumSize.Width, pageHeader.ClientSize.Width - 80);
        _pageTitle.Width = contentWidth;
        _pageSubtitle.Width = contentWidth;
        _workflow.Width = contentWidth;
        _nextAction.Width = contentWidth;
    }

    private void OnCheckpointSaveActivity(object? sender, CheckpointSaveEventArgs e)
    {
        if (_session is null || e.SessionId != _session.SessionId || IsDisposed) return;
        void UpdateStatus()
        {
            if (IsDisposed) return;
            _saveStatus.Text = e.Success
                ? $"자동 저장됨 {e.Timestamp:HH:mm:ss}"
                : "자동 저장 실패 · 수동 저장 필요";
            _saveStatus.ForeColor = e.Success ? Colors.Success : Colors.Danger;
            _saveStatus.AccessibleDescription = e.Success ? e.Path : e.Message;
        }

        if (InvokeRequired && IsHandleCreated) BeginInvoke(UpdateStatus);
        else UpdateStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) InspectionCheckpointService.SaveActivity -= OnCheckpointSaveActivity;
        base.Dispose(disposing);
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
        if (_session is not null) _checkpoints.TrySave(_session);
        foreach (var pair in _menus) pair.Value.IsSelected = pair.Key == key;
        _pageTitle.Text = title;
        _pageSubtitle.Text = subtitle;
        page.Visible = false;
        page.Dock = DockStyle.Fill;
        _content.SuspendLayout();
        var previousPages = _content.Controls.Cast<Control>().ToArray();
        _content.Controls.Clear();
        _content.Controls.Add(page);
        _content.ResumeLayout(true);
        page.Visible = true;
        page.BringToFront();
        _content.Invalidate(true);
        foreach (var previous in previousPages) previous.Dispose();
        RefreshWorkflow();
    }

    protected void ConfigureConnectionReadiness(Func<(bool Ready, string Detail)> provider)
    {
        _connectionReadiness = provider;
        RefreshWorkflow();
    }

    protected void RefreshWorkflow()
    {
        if (_session is null) return;
        var readiness = _connectionReadiness();
        var snapshot = InspectionWorkflowService.Evaluate(_session, readiness.Ready, readiness.Detail);
        _workflow.SetSteps(snapshot.Steps);
        _nextAction.Text = $"필수 검사 {snapshot.CompletedItems}/{snapshot.TotalItems} 완료  ·  {snapshot.NextAction}";
        _nextAction.ForeColor = snapshot.Steps.Any(step => step.State == WorkflowStageState.Attention)
            ? Colors.Warning
            : snapshot.Steps.All(step => step.State == WorkflowStageState.Complete)
                ? Colors.Success
                : Colors.Primary;
    }

    protected void SetTopStatus(string text, Color color)
    {
        _connectionSummary.Text = string.IsNullOrEmpty(_sessionPrefix)
            ? $"●  {text}"
            : $"{_sessionPrefix}  |  ●  {text}";
        _connectionSummary.ForeColor = color;
    }

    protected abstract void Navigate(string key);

    protected void RequestContinuation(InspectionContinuationAction action)
    {
        CompletionAction = action;
        Close();
    }

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

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
