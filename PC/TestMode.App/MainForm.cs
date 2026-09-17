namespace TestMode.App;

internal sealed class MainForm : Form
{
    private readonly Panel host;
    private readonly Label breadcrumb;
    private readonly Label connection;
    private readonly Label stageBanner;
    private readonly bool preview;
    private readonly UsbTestClient usb = new();
    private readonly System.Windows.Forms.Timer deviceTimer = new() { Interval = 100 };
    private bool devicePollInProgress;
    private SwitchView? switchView;
    private FaultView? faultView;
    internal Page CurrentPage { get; private set; }
    internal Board CurrentBoard { get; private set; } = Board.Main;

    public MainForm(bool preview = false)
    {
        this.preview = preview;
        Name = "TestModeMain";
        Text = "TestMode · MAIN / DIO";
        Font = Ui.Body;
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 850);
        MinimumSize = new Size(1100, 800);
        var top = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Color.White, Padding = new Padding(30, 17, 28, 14) };
        var brand = Ui.Label("TestMode", Ui.Logo, Ui.Blue);
        brand.Dock = DockStyle.Left;
        brand.Width = 160;
        var home = Ui.Button("goHome", "홈으로", () => Navigate(Page.Home));
        home.Dock = DockStyle.Right;
        home.Width = 120;
        breadcrumb = Ui.Label("", color: Ui.Muted);
        breadcrumb.Dock = DockStyle.Fill;
        breadcrumb.AutoSize = false;
        breadcrumb.TextAlign = ContentAlignment.MiddleLeft;
        breadcrumb.Padding = new Padding(20, 0, 0, 0);
        top.Controls.Add(breadcrumb);
        top.Controls.Add(home);
        top.Controls.Add(brand);
        stageBanner = Ui.Note(preview ? "미리보기 모드 · 표시된 값은 실제 보드 데이터가 아닙니다." : "USB를 연결하면 자동으로 인식합니다.");
        stageBanner.Height = 44;
        stageBanner.BackColor = preview ? Color.FromArgb(255, 245, 224) : Ui.PaleBlue;
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(30, 8, 30, 0), BackColor = Color.White };
        connection = Ui.Label("●  장치 연결 안 됨", Ui.Small, Ui.Muted);
        connection.Name = "deviceStatus";
        connection.Dock = DockStyle.Left;
        var version = Ui.Label("MAIN / DIO   ·   TEST MODE", Ui.Small, Ui.Muted);
        version.Dock = DockStyle.Right;
        footer.Controls.Add(connection);
        footer.Controls.Add(version);
        host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 26, 30, 26), AutoScroll = true };
        Controls.Add(host);
        Controls.Add(footer);
        Controls.Add(stageBanner);
        Controls.Add(top);
        Navigate(Page.Home);
        deviceTimer.Tick += async (_, _) => await PollDeviceAsync();
        deviceTimer.Start();
    }

    internal void Navigate(Page page, Board? board = null)
    {
        if (board.HasValue) CurrentBoard = board.Value;
        if (page == Page.Fault && CurrentBoard == Board.Dio) page = Page.Switch;
        CurrentPage = page;
        switchView = null;
        faultView = null;
        host.SuspendLayout();
        foreach (Control control in host.Controls.Cast<Control>().ToArray()) { host.Controls.Remove(control); control.Dispose(); }
        connection.Text = "●  장치 연결 안 됨";
        breadcrumb.Text = page switch
        {
            Page.Home => "보드 검사 및 펌웨어 업데이트",
            Page.Boards => "TEST 모드  /  대상 선택",
            Page.Bootloader => "부트로더  /  펌웨어 업데이트",
            _ => $"TEST 모드  /  {BoardName}"
        };
        if (page == Page.Home) BuildSelection(false);
        else if (page == Page.Boards) BuildSelection(true);
        else BuildWorkspace(page);
        host.ResumeLayout(true);
    }

    private string BoardName => CurrentBoard == Board.Main ? "MAIN" : "DIO";

    private void BuildSelection(bool boards)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(44, 24, 44, 16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        var title = Ui.Label(boards ? "검사할 보드를 선택하세요" : "실행할 모드를 선택하세요", Ui.Heading);
        title.Dock = DockStyle.Top;
        title.AutoSize = false;
        title.Height = 54;
        title.TextAlign = ContentAlignment.MiddleCenter;
        var subtitle = Ui.Label(boards ? "연결할 보드에 맞는 TEST 화면으로 이동합니다." : "실행할 검사 모드를 선택합니다.", color: Ui.Blue);
        subtitle.AutoSize = false;
        subtitle.Dock = DockStyle.Bottom;
        subtitle.Height = 44;
        subtitle.TextAlign = ContentAlignment.TopCenter;
        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        layout.Controls.Add(heading, 0, 0);
        var cards = Ui.Table(2, 50, 50);
        if (boards)
        {
            cards.Controls.Add(SelectionCard("M", "MAIN", "로터리 스위치와 FAULT 출력을 확인합니다.", "로터리 상태\nOPEN (FAULT) / CLOSE (NORMAL)\n펌웨어 업데이트 검사", "selectMain", "MAIN 검사 시작", Ui.Teal, () => Navigate(Page.Switch, Board.Main)), 0, 0);
            cards.Controls.Add(SelectionCard("D", "DIO", "ID 설정 DIP 스위치의 상태를 확인합니다.", "ID DIP 상태\n현재 ID 표시\n펌웨어 업데이트 검사", "selectDio", "DIO 검사 시작", Ui.Orange, () => Navigate(Page.Switch, Board.Dio)), 1, 0);
        }
        else
        {
            cards.Controls.Add(SelectionCard("↑", "부트로더", "HEX 파일을 선택해 펌웨어를 업데이트합니다.", "MAIN / DIO 선택\nHEX 파일 또는 폴더 선택\n업데이트 진행 및 결과", "selectBootloader", "부트로더 시작", Ui.Blue, () => Navigate(Page.Bootloader)), 0, 0);
            cards.Controls.Add(SelectionCard("◎", "TEST 모드", "보드의 스위치와 FAULT 출력 상태를 확인합니다.", "물리 스위치 상태 확인\nMAIN FAULT 출력 제어\n펌웨어 업데이트 검사", "selectTest", "TEST 모드 시작", Ui.Red, () => Navigate(Page.Boards)), 1, 0);
        }
        layout.Controls.Add(cards, 0, 1);
        var foot = Ui.Label("실행할 항목을 선택하면 해당 화면이 열립니다.", Ui.Small, Ui.Muted);
        foot.AutoSize = false;
        foot.Dock = DockStyle.Fill;
        foot.TextAlign = ContentAlignment.BottomCenter;
        layout.Controls.Add(foot, 0, 2);
        host.Controls.Add(layout);
    }

    private static Surface SelectionCard(string glyph, string title, string description, string features, string name, string buttonText, Color color, Action click)
    {
        var card = new Surface { Dock = DockStyle.Fill, Edge = color, Padding = Padding.Empty, Margin = new Padding(10, 0, 10, 0) };
        var iconBackground = color == Ui.Blue ? Ui.PaleBlue : color == Ui.Red ? Ui.PaleRed : color == Ui.Teal ? Ui.PaleTeal : Ui.PaleOrange;
        var icon = new SelectionIcon { Glyph = glyph, Accent = color, BackColor = iconBackground, Location = new Point(24, 24), Size = new Size(90, 90) };
        var headline = Ui.Label(title, new Font("Segoe UI", 18f, FontStyle.Bold), color);
        headline.Location = new Point(136, 30);
        headline.AutoSize = false;
        headline.Size = new Size(420, 32);
        var desc = Ui.Label(description, Ui.Body, Ui.Muted);
        desc.Location = new Point(136, 66);
        desc.AutoSize = false;
        desc.Size = new Size(420, 48);
        var divider = new Panel { Location = new Point(24, 150), Height = 1, BackColor = Ui.Border };
        var labels = features.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var button = Ui.Button(name, buttonText + "  →", click, true, color);
        card.SizeChanged += (_, _) =>
        {
            var usable = Math.Max(180, card.ClientSize.Width - 48);
            var gap = 8;
            var tileWidth = Math.Max(70, (usable - gap * (labels.Length - 1)) / Math.Max(1, labels.Length));
            var tileIndex = 0;
            foreach (Control control in card.Controls)
            {
                if (control is SelectionFeatureTile tile)
                {
                    tile.Location = new Point(24 + tileIndex * (tileWidth + gap), 174);
                    tile.Size = new Size(tileWidth, 112);
                    tileIndex++;
                }
            }
            divider.Width = usable;
            button.Location = new Point(24, Math.Max(300, card.ClientSize.Height - 80));
            button.Size = new Size(usable, 56);
        };
        card.Controls.Add(icon);
        card.Controls.Add(headline);
        card.Controls.Add(desc);
        card.Controls.Add(divider);
        for (var index = 0; index < labels.Length; index++)
        {
            var tile = new SelectionFeatureTile { Accent = color, Text = labels[index], Location = new Point(24, 174), Size = new Size(112, 112) };
            card.Controls.Add(tile);
        }
        button.Location = new Point(24, 332);
        button.Size = new Size(400, 56);
        button.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        button.Height = 56;
        card.Controls.Add(button);
        return card;
    }

    private sealed class SelectionIcon : Control
    {
        public string Glyph { get; set; } = "•";
        public Color Accent { get; set; } = Ui.Blue;

        public SelectionIcon()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillEllipse(brush, ClientRectangle);
            TextRenderer.DrawText(e.Graphics, Glyph, Ui.CardIcon, ClientRectangle, Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private sealed class SelectionFeatureTile : Control
    {
        public Color Accent { get; set; } = Ui.Blue;

        public SelectionFeatureTile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = Surface.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 10);
            using var brush = new SolidBrush(Color.White);
            using var pen = new Pen(Ui.Border);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            using var dot = new SolidBrush(Accent);
            e.Graphics.FillEllipse(dot, new Rectangle(Width / 2 - 7, 18, 14, 14));
            TextRenderer.DrawText(e.Graphics, Text, Ui.Small, new Rectangle(5, 48, Width - 10, Height - 58), Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.Top);
        }
    }

    private void BuildWorkspace(Page page)
    {
        var container = new Panel { Dock = DockStyle.Fill };
        var connectionBar = BuildConnectionBar(page == Page.Bootloader);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 18, 0, 0) };
        Control view = page switch
        {
            Page.Switch => new SwitchView(CurrentBoard),
            Page.Fault => new FaultView(normal => _ = SendFaultAsync(normal)),
            _ => new FirmwareView(page != Page.Bootloader, CurrentBoard == Board.Main, usb)
        };
        switchView = view as SwitchView;
        faultView = view as FaultView;
        view.Name = "activeView";
        body.Controls.Add(view);
        if (page != Page.Bootloader)
        {
            var menu = new Panel { Dock = DockStyle.Left, Width = 210, Padding = new Padding(0, 6, 20, 0) };
            var heading = Ui.Label(BoardName, Ui.Heading);
            var switches = MenuButton("navSwitch", "스위치 상태", Page.Switch, page);
            var firmware = MenuButton("navFirmware", "펌웨어 검사", Page.Firmware, page);
            var back = Ui.Button("chooseBoard", "← 보드 선택", () => Navigate(Page.Boards));
            back.Dock = DockStyle.Bottom;
            back.Width = 184;
            var stack = Ui.Stack(heading, switches);
            if (CurrentBoard == Board.Main) stack.Controls.Add(MenuButton("navFault", "FAULT 출력", Page.Fault, page));
            stack.Controls.Add(firmware);
            menu.Controls.Add(stack);
            menu.Controls.Add(back);
            body.Controls.Add(menu);
        }
        container.Controls.Add(body);
        container.Controls.Add(connectionBar);
        host.Controls.Add(container);
    }

    private Button MenuButton(string name, string text, Page target, Page current)
    {
        var button = Ui.Button(name, text, () => Navigate(target), current == target);
        button.Width = 184;
        button.Height = 48;
        button.Margin = new Padding(0, 8, 0, 0);
        return button;
    }

    private Surface BuildConnectionBar(bool chooseTarget)
    {
        var card = new Surface { Dock = DockStyle.Top, Height = 88, Padding = new Padding(20), Margin = Padding.Empty };
        var label = Ui.Label("대상 보드", Ui.Bold);
        label.Margin = new Padding(0, 10, 12, 0);
        var target = Ui.Label(BoardName, Ui.Bold, Ui.Blue);
        target.Name = "fixedBoardTarget";
        target.AutoSize = false;
        target.Size = new Size(115, 34);
        target.Margin = new Padding(0, 6, 24, 0);
        target.TextAlign = ContentAlignment.MiddleLeft;
        var port = Ui.Label("USB 자동 인식", color: Ui.Muted);
        port.Name = "portSelection";
        port.AutoSize = false;
        port.Size = new Size(220, 34);
        port.Padding = new Padding(10, 6, 0, 0);
        port.BackColor = Ui.Background;
        port.Margin = new Padding(0, 6, 12, 0);
        var state = Ui.Label("●  연결 대기", color: Ui.Muted);
        state.Margin = new Padding(8, 10, 0, 0);
        Control targetControl = target;
        if (chooseTarget)
        {
            target.Dispose();
            var main = Ui.Button("targetMain", "MAIN", () => Navigate(Page.Bootloader, Board.Main), CurrentBoard == Board.Main);
            var dio = Ui.Button("targetDio", "DIO", () => Navigate(Page.Bootloader, Board.Dio), CurrentBoard == Board.Dio);
            main.Width = dio.Width = 78;
            main.Margin = new Padding(0, 0, 6, 0);
            dio.Margin = Padding.Empty;
            targetControl = Ui.Row(main, dio);
            targetControl.Margin = new Padding(0, 0, 24, 0);
        }
        card.Controls.Add(Ui.Row(label, targetControl, port, state));
        return card;
    }

    private async Task SendFaultAsync(bool normal)
    {
        if (faultView is null)
            return;

        var success = await usb.SetFaultAsync(normal);
        if (IsDisposed || faultView is null)
            return;
        faultView.SetCommandResult(normal, success);
        SetConnectionStatus(success);
    }

    private async Task PollDeviceAsync()
    {
        if (preview || devicePollInProgress ||
            (CurrentPage != Page.Switch && !(CurrentBoard == Board.Main && CurrentPage == Page.Fault)))
            return;

        devicePollInProgress = true;
        try
        {
            var connected = await usb.EnsureConnectedAsync(CurrentBoard);
            SetConnectionStatus(connected);
            if (!connected)
            {
                switchView?.SetDisconnected();
                faultView?.SetConnected(false);
                return;
            }

            faultView?.SetConnected(true);
            if (CurrentPage == Page.Switch && switchView is not null && CurrentBoard == Board.Main)
            {
                var value = await usb.ReadRotaryAsync();
                if (value.HasValue)
                    switchView.SetDeviceValue(value.Value);
                else
                    switchView.SetDisconnected();
            }
            else if (CurrentPage == Page.Switch && switchView is not null && CurrentBoard == Board.Dio)
            {
                var switches = await usb.ReadDioSwitchAsync();
                if (switches.HasValue)
                    switchView.SetDioDeviceValue(switches.Value.Switch1, switches.Value.Switch2);
                else
                    switchView.SetDisconnected();
            }
            else if (CurrentPage == Page.Fault && faultView?.RequestedNormal is bool normal)
            {
                await usb.SetFaultAsync(normal);
            }
        }
        finally
        {
            devicePollInProgress = false;
        }
    }

    private void SetConnectionStatus(bool connected)
    {
        if (IsDisposed)
            return;
        connection.Text = connected ? "●  USB 연결됨" : "●  USB 연결 안 됨";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            deviceTimer.Stop();
            deviceTimer.Dispose();
            usb.Dispose();
        }
        base.Dispose(disposing);
    }

    internal void PreviewConnection(string message)
    {
        if (!preview) throw new InvalidOperationException("Preview data requires --ui-check.");
        connection.Text = "예시 · " + message;
    }
}
