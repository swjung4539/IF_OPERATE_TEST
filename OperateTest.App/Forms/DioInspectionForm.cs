using OperateTest.App.Controls;
using OperateTest.App.Models;
using OperateTest.App.Services;
using OperateTest.App.Services.DioInspection;
using OperateTest.App.Services.StLink;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class DioInspectionForm : InspectionShellForm
{
    private readonly ISerialConnectionManager _connections;
    private readonly IDioLoopbackService _loopback;
    private readonly IDioIdSwitchReader _idReader;
    private readonly InspectionSession _session;
    private CancellationTokenSource? _testCts;
    private byte? _activeSlaveId;
    private ushort? _lastBaseline;
    private bool _testRunning;
    private bool _closing;

    public DioInspectionForm(ISerialConnectionManager connections, IInspectionProtocolServices protocols, IDioIdSwitchReader idReader, InspectionSession session)
        : base("DIO 보드 검사", "DO·DI 루프백 · ID DIP 스위치", Colors.Warning, showSettings: false, session: session)
    {
        _connections = connections;
        _loopback = new DioLoopbackService(protocols);
        _idReader = idReader;
        _session = session;
        Shown += (_, _) => Navigate("dashboard");
        FormClosing += OnClosing;
    }

    protected override void Navigate(string key)
    {
        if (_testRunning && key != "test")
        {
            MessageBox.Show(this, "DIO 루프백 검사를 먼저 중지하세요.", "DIO 검사", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        switch (key)
        {
            case "result": ShowPage(key, "최종 판정", "필수 검사 완료 여부와 DO 전체 OFF 안전 상태를 확인하고 최종 판정을 확정합니다.", new InspectionSummaryControl(_session)); break;
            case "settings": ShowPage(key, "MAIN UART4 연결", "DIO 루프백 명령을 전달할 MAIN UART4 포트를 연결합니다.", BuildSettingsPage()); break;
            case "test": ShowPage(key, "DIO 통신 검사", "DO 출력과 루프백된 DI 입력을 채널별로 판정합니다.", BuildTestPage()); break;
            case "manual": ShowPage(key, "육안 및 전원 검사", "작업자가 직접 확인한 육안 상태와 전원 측정 결과를 입력합니다.", new ManualInspectionControl(_session)); break;
            case "fault-switch": ShowPage(key, "FAULT 및 스위치 검사", "DIO 보드는 ID DIP 스위치 검사 항목을 제공합니다.", BuildSwitchPage()); break;
            default: ShowPage("dashboard", "DIO 보드 검사 대시보드", "통신 연결과 루프백·ID 스위치 검사 준비 상태를 확인합니다.", BuildDashboard()); break;
        }
        var connected = _connections.Get(ChannelDefinitions.Uart4.Key).IsConnected;
        SetTopStatus(connected ? "MAIN UART4 연결됨" : "MAIN UART4 연결 안 됨", connected ? Colors.Success : Colors.TextMuted);
    }

    private static Panel BasePage() => new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Colors.AppBackground };

    private Control BuildDashboard()
    {
        var page = BasePage();
        var connected = _connections.Get(ChannelDefinitions.Uart4.Key).IsConnected;
        page.Controls.Add(Card(30, 12, 286, "MAIN UART4", connected ? "연결됨" : "연결 안 됨", AppIcon.Keyboard, connected ? Colors.Success : Colors.TextMuted));
        page.Controls.Add(Card(332, 12, 286, "루프백 채널", "16채널", AppIcon.Swap, Colors.Primary));
        page.Controls.Add(Card(634, 12, 286, "ID DIP", "ID 1~4", AppIcon.Settings, Colors.Warning));
        page.Controls.Add(Card(936, 12, 286, "출력 안전", "종료 시 전체 OFF", AppIcon.Shield, Colors.Success));

        var topology = new AppPanel { Location = new Point(30, 136), Size = new Size(780, 280) };
        topology.Controls.Add(Title("DIO 루프백 검사 구성", 24, 20, 400));
        topology.Controls.Add(Caption("PC 명령으로 DO를 출력하고 커넥터를 통해 돌아온 DI를 비교합니다.", 24, 50, 680));
        var nodes = new[] { ("PC 검사", AppIcon.Monitor), ("RS-485", AppIcon.Network), ("DIO DO", AppIcon.Server), ("루프백 지그", AppIcon.Swap), ("DIO DI", AppIcon.Server) };
        for (var i = 0; i < nodes.Length; i++)
        {
            var x = 30 + i * 142;
            topology.Controls.Add(new CircleIcon { Icon = nodes[i].Item2, Accent = i == 3 ? Colors.Warning : Colors.Primary, Location = new Point(x + 28, 108), Size = new Size(58, 58) });
            topology.Controls.Add(new Label { Location = new Point(x, 177), Size = new Size(114, 24), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, TextAlign = ContentAlignment.MiddleCenter, Text = nodes[i].Item1 });
            if (i < nodes.Length - 1) topology.Controls.Add(new Panel { Location = new Point(x + 100, 137), Size = new Size(52, 1), BackColor = Colors.BorderStrong });
        }
        page.Controls.Add(topology);

        var ready = new AppPanel { Location = new Point(832, 136), Size = new Size(390, 280) };
        ready.Controls.Add(Title("검사 준비 상태", 24, 20, 260));
        AddReady(ready, 76, "MAIN UART4", connected ? "연결됨" : "연결 필요", connected);
        AddReady(ready, 132, "루프백 커넥터", "검사자가 연결 확인", true);
        AddReady(ready, 188, "ST-LINK", _idReader.CliPath is null ? "CLI 없음" : "CLI 확인", _idReader.CliPath is not null);
        ready.Controls.Add(Caption("LED 점등 상태는 검사자가 직접 확인합니다.", 24, 242, 330));
        page.Controls.Add(ready);

        var quick = new AppPanel { Location = new Point(30, 438), Size = new Size(1192, 178) };
        quick.Controls.Add(Title("빠른 실행", 24, 20, 250));
        quick.Controls.Add(Caption("육안·전원 확인 후 루프백과 ID DIP 검사를 순서대로 진행하세요.", 24, 50, 900));
        var manual = new AppButton { Variant = AppButtonVariant.Warning, IconGlyph = AppIcon.Clipboard, Text = "육안·전원 검사", Location = new Point(24, 96), Size = new Size(250, 48) };
        var mainLink = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Network, Text = "MAIN UART4 연결", Location = new Point(290, 96), Size = new Size(250, 48) };
        var test = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Play, Text = "루프백 검사", Location = new Point(556, 96), Size = new Size(250, 48) };
        var id = new AppButton { Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.Settings, Text = "ID 스위치 검사", Location = new Point(822, 96), Size = new Size(250, 48) };
        manual.Click += (_, _) => Navigate("manual"); mainLink.Click += (_, _) => Navigate("settings"); test.Click += (_, _) => Navigate("test"); id.Click += (_, _) => Navigate("fault-switch");
        quick.Controls.Add(manual); quick.Controls.Add(mainLink); quick.Controls.Add(test); quick.Controls.Add(id); page.Controls.Add(quick);
        return page;
    }

    private Control BuildSettingsPage()
    {
        var page = BasePage();
        var card = new AppPanel { Location = new Point(30, 12), Size = new Size(1192, 238) };
        card.Controls.Add(Title("1. MAIN UART4 연결", 24, 20, 400));
        card.Controls.Add(Caption("PC와 MAIN UART4를 연결합니다. DIO에는 별도 COM 포트를 연결하지 않습니다.", 24, 51, 850));
        var row = new ConnectionRow(_connections, ChannelDefinitions.Uart4) { Location = new Point(24, 104), Size = new Size(1138, 66) };
        var refresh = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Refresh, Text = "포트 새로고침", Location = new Point(994, 20), Size = new Size(168, 40) };
        refresh.Click += (_, _) => row.RefreshPorts();
        card.Controls.Add(refresh); card.Controls.Add(row); page.Controls.Add(card);

        var note = new AppPanel { Location = new Point(30, 270), Size = new Size(1192, 150) };
        note.Controls.Add(Title("명령 전달 경로", 24, 20, 300));
        note.Controls.Add(Caption("PC → MAIN UART4 → MAIN 내부 DIO 통신 → DIO 보드 순서로 명령이 전달됩니다.", 24, 56, 900));
        note.Controls.Add(Caption("ID DIP의 물리 상태는 통신 경로와 별개로 ST-LINK 전용 검사창에서 확인합니다.", 24, 84, 900));
        page.Controls.Add(note);
        return page;
    }

    private Control BuildTestPage()
    {
        var page = BasePage();
        var start = new AppButton { Variant = AppButtonVariant.Primary, IconGlyph = AppIcon.Play, Text = "START", Location = new Point(712, 12), Size = new Size(150, 48) };
        var stop = new AppButton { Variant = AppButtonVariant.Danger, Text = "STOP", Location = new Point(874, 12), Size = new Size(150, 48), Enabled = false };
        var reset = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Refresh, Text = "RESET", Location = new Point(1036, 12), Size = new Size(150, 48) };
        page.Controls.Add(start); page.Controls.Add(stop); page.Controls.Add(reset);

        var repeatCard = new AppPanel { Location = new Point(30, 12), Size = new Size(650, 88) };
        repeatCard.Controls.Add(Caption("반복 횟수", 20, 11, 100));
        var repeatInput = new AppTextBox { Location = new Point(20, 37), Size = new Size(116, 34), TextValue = "1", TextAlign = HorizontalAlignment.Center };
        var forever = new AppToggleSwitch { Location = new Point(238, 41), IsOn = false };
        var enabled = new AppToggleSwitch { Location = new Point(450, 41), IsOn = true };
        repeatCard.Controls.Add(repeatInput); repeatCard.Controls.Add(new Label { Location = new Point(145, 39), Size = new Size(40, 30), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = "회" });
        repeatCard.Controls.Add(forever); repeatCard.Controls.Add(new Label { Location = new Point(302, 39), Size = new Size(110, 28), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = "계속 반복" });
        repeatCard.Controls.Add(enabled); repeatCard.Controls.Add(new Label { Location = new Point(514, 39), Size = new Size(120, 28), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = "루프백 검사 ON" });
        page.Controls.Add(repeatCard);

        var totalCard = Card(30, 116, 280, "TOTAL", "0", AppIcon.Clipboard, Colors.TextPrimary);
        var passCard = Card(326, 116, 280, "PASS", "0", AppIcon.Shield, Colors.Success);
        var failCard = Card(622, 116, 280, "FAIL", "0", AppIcon.Warning, Colors.Danger);
        var errorCard = Card(918, 116, 280, "ERROR", "0", AppIcon.Info, Colors.Warning);
        page.Controls.Add(totalCard); page.Controls.Add(passCard); page.Controls.Add(failCard); page.Controls.Add(errorCard);

        var channels = new AppPanel { Location = new Point(30, 240), Size = new Size(800, 434) };
        channels.Controls.Add(Title("DO → DI 채널 판정", 20, 16, 330));
        var current = new Label { Location = new Point(20, 48), Size = new Size(750, 25), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, Text = "검사 대기" };
        var values = new Label { Location = new Point(20, 75), Size = new Size(750, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "Baseline: ----   현재 DI: ----   예상 변화: ----   실제 변화: ----" };
        channels.Controls.Add(current); channels.Controls.Add(values);
        var grid = new TableLayoutPanel { Location = new Point(18, 112), Size = new Size(764, 300), ColumnCount = 4, RowCount = 4, BackColor = Color.Transparent };
        for (var i = 0; i < 4; i++) { grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25)); }
        var labels = new Label[16];
        for (var i = 0; i < 16; i++)
        {
            labels[i] = new Label { Dock = DockStyle.Fill, Margin = new Padding(5), BackColor = Colors.SurfaceBlue, ForeColor = Colors.TextMuted, Font = Fonts.ButtonText, TextAlign = ContentAlignment.MiddleCenter, Text = $"DO{i + 1} → DI{i + 1}\n미실행" };
            grid.Controls.Add(labels[i], i % 4, i / 4);
        }
        channels.Controls.Add(grid); page.Controls.Add(channels);

        var result = new AppPanel { Location = new Point(850, 240), Size = new Size(348, 434) };
        result.Controls.Add(Title("검사 결과", 20, 16, 240));
        const string initialGuide = "RS-485와 루프백 커넥터 연결 후\nSTART를 누르세요.\n\nLED 점등은 직접 확인합니다.";
        var summary = new Label { Location = new Point(20, 58), Size = new Size(308, 260), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = initialGuide };
        var runStatus = new Label { Location = new Point(20, 338), Size = new Size(308, 42), Font = Fonts.ButtonText, ForeColor = Colors.TextMuted, Text = "검사 대기" };
        var off = new AppButton { Variant = AppButtonVariant.OutlinePrimary, Text = "DO 전체 OFF", Location = new Point(20, 386), Size = new Size(150, 38) };
        result.Controls.Add(summary); result.Controls.Add(runStatus); result.Controls.Add(off); page.Controls.Add(result);

        var total = 0; var passed = 0; var failed = 0; var errors = 0;
        void UpdateCards() { totalCard.CardValue = total.ToString(); passCard.CardValue = passed.ToString(); failCard.CardValue = failed.ToString(); errorCard.CardValue = errors.ToString(); }
        void ResetUi()
        {
            total = passed = failed = errors = 0; UpdateCards(); current.Text = "검사 대기"; runStatus.Text = "검사 대기"; summary.Text = initialGuide;
            _session.Results.DioInputPassed = null;
            _session.Results.DioOutputPassed = null;
            _session.Results.DioLoopbackStatus = InspectionExecutionStatus.NotRun;
            _session.Results.DioSafetyStatus = DioSafetyStatus.NotChecked;
            _lastBaseline = null;
            for (var i = 0; i < 16; i++) { labels[i].Text = $"DO{i + 1} → DI{i + 1}\n미실행"; labels[i].BackColor = Colors.SurfaceBlue; labels[i].ForeColor = Colors.TextMuted; }
        }
        async Task AllOff()
        {
            if (!_connections.Get(ChannelDefinitions.Uart4.Key).IsConnected)
            {
                MessageBox.Show(this, "COMM 보드의 MAIN UART4 COM 포트를 먼저 연결하세요.", "통신 연결 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                var id = _activeSlaveId ?? await _loopback.FindSlaveIdAsync(); _activeSlaveId = id;
                _session.Results.DioSafetyStatus = DioSafetyStatus.Restoring;
                var safety = await _loopback.EnsureAllOutputsOffAsync(id, _lastBaseline);
                _session.Results.DioSafetyStatus = safety.Verified ? DioSafetyStatus.OffVerified : DioSafetyStatus.CommandSent;
                summary.Text = safety.Detail;
                summary.ForeColor = safety.Verified ? Colors.Success : Colors.Warning;
            }
            catch (Exception ex) { _session.Results.DioSafetyStatus = DioSafetyStatus.Failed; summary.Text = $"전체 OFF 실패: {ex.Message}"; summary.ForeColor = Colors.Danger; }
        }

        off.Click += async (_, _) => await AllOff();
        stop.Click += (_, _) => _testCts?.Cancel();
        reset.Click += (_, _) => ResetUi();
        start.Click += async (_, _) =>
        {
            if (!enabled.IsOn) { MessageBox.Show(this, "루프백 검사 항목을 ON으로 설정하세요.", "DIO 검사", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!_connections.Get(ChannelDefinitions.Uart4.Key).IsConnected) { MessageBox.Show(this, "COMM 보드의 MAIN UART4 COM 포트를 먼저 연결하세요.", "DIO 검사", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!int.TryParse(repeatInput.TextValue, out var repeat) || repeat < 1) { repeatInput.HasError = true; MessageBox.Show(this, "반복 횟수는 1 이상의 숫자여야 합니다.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            repeatInput.HasError = false; _session.Results.InvalidateFinalization(); _testCts?.Dispose(); _testCts = new CancellationTokenSource(); _testRunning = true;
            _session.Results.DioLoopbackStatus = InspectionExecutionStatus.Running;
            _session.Results.DioSafetyStatus = DioSafetyStatus.NotChecked;
            start.Enabled = reset.Enabled = false; stop.Enabled = true; repeatInput.Enabled = forever.Enabled = enabled.Enabled = false;
            var round = 0;
            try
            {
                _activeSlaveId = await _loopback.FindSlaveIdAsync(_testCts.Token);
                while (!_testCts.IsCancellationRequested && (forever.IsOn || round < repeat))
                {
                    round++; runStatus.Text = forever.IsOn ? $"반복 {round}회 실행 중" : $"{round}/{repeat}회 실행 중"; runStatus.ForeColor = Colors.Primary;
                    var progress = new Progress<DioLoopbackProgress>(value =>
                    {
                        _lastBaseline = value.Baseline;
                        current.Text = value.Verdict == DioChannelVerdict.Running ? $"검사 중: DO {value.Channel} → DI {value.Channel}" : $"완료 {value.Completed}/16: DO {value.Channel} → DI {value.Channel}";
                        values.Text = $"Baseline: 0x{value.Baseline:X4}   현재 DI: 0x{value.Current:X4}   예상 변화: 0x{value.ExpectedChange:X4}   실제 변화: 0x{value.ActualChange:X4}";
                        var label = labels[value.Channel - 1]; label.Text = $"DO{value.Channel} → DI{value.Channel}\n{(value.Verdict == DioChannelVerdict.Pass ? "PASS" : value.Verdict == DioChannelVerdict.Fail ? "FAIL" : "검사 중")}";
                        label.BackColor = value.Verdict == DioChannelVerdict.Pass ? Colors.SuccessSurface : value.Verdict == DioChannelVerdict.Fail ? Colors.DangerSurface : Colors.SurfaceBlue;
                        label.ForeColor = value.Verdict == DioChannelVerdict.Pass ? Colors.Success : value.Verdict == DioChannelVerdict.Fail ? Colors.Danger : Colors.Primary;
                    });
                    var response = await _loopback.RunAsync(progress, _testCts.Token); _activeSlaveId = response.SlaveId; _lastBaseline = response.Baseline; total++;
                    if (response.Passed) passed++; else failed++; UpdateCards();
                    summary.Text = $"DIO 슬롯 {response.SlaveId}\n\n{response.Detail}\n\n전체 ON: {(response.AllOnPassed ? "정상" : "실패")}\n전체 OFF 복귀: {(response.OutputsCleared ? "정상" : "실패")}";
                    summary.ForeColor = response.Passed ? Colors.Success : Colors.Danger;
                }
                _testCts.Token.ThrowIfCancellationRequested();
                runStatus.Text = $"검사 완료 · 총 {round}회 반복"; runStatus.ForeColor = failed == 0 && errors == 0 ? Colors.Success : Colors.Danger;
                if (round > 0)
                {
                    var passedAll = failed == 0 && errors == 0;
                    _session.Results.DioInputPassed = passedAll;
                    _session.Results.DioOutputPassed = passedAll;
                    _session.Results.DioLoopbackStatus = passedAll ? InspectionExecutionStatus.Pass : InspectionExecutionStatus.Fail;
                }
            }
            catch (OperationCanceledException) { _session.Results.DioInputPassed = null; _session.Results.DioOutputPassed = null; _session.Results.DioLoopbackStatus = InspectionExecutionStatus.Stopped; runStatus.Text = "사용자에 의해 중지됨"; runStatus.ForeColor = Colors.TextMuted; }
            catch (Exception ex) { _session.Results.DioInputPassed = null; _session.Results.DioOutputPassed = null; _session.Results.DioLoopbackStatus = InspectionExecutionStatus.Error; total++; errors++; UpdateCards(); summary.Text = $"검사 오류\n\n{ex.Message}"; summary.ForeColor = Colors.Warning; runStatus.Text = "검사 오류"; }
            finally
            {
                if (_connections.Get(ChannelDefinitions.Uart4.Key).IsConnected && _activeSlaveId.HasValue)
                {
                    try
                    {
                        _session.Results.DioSafetyStatus = DioSafetyStatus.Restoring;
                        var safety = await _loopback.EnsureAllOutputsOffAsync(_activeSlaveId.Value, _lastBaseline, CancellationToken.None);
                        _session.Results.DioSafetyStatus = safety.Verified ? DioSafetyStatus.OffVerified : DioSafetyStatus.CommandSent;
                        summary.Text += $"\n\n안전 종료: {safety.Detail}";
                    }
                    catch (Exception ex)
                    {
                        _session.Results.DioSafetyStatus = DioSafetyStatus.Failed;
                        summary.Text += $"\n\n안전 종료 실패: {ex.Message}";
                        summary.ForeColor = Colors.Danger;
                    }
                }
                new InspectionCheckpointService().TrySave(_session);
                _testRunning = false; start.Enabled = reset.Enabled = true; stop.Enabled = false; repeatInput.Enabled = forever.Enabled = enabled.Enabled = true;
            }
        };
        return page;
    }

    private Control BuildSwitchPage()
    {
        var page = BasePage();
        var card = new AppPanel { Location = new Point(30, 12), Size = new Size(1192, 230) };
        card.Controls.Add(Title("검사 항목", 24, 20, 300)); card.Controls.Add(Caption("열기 버튼을 누르면 ID DIP 전용 검사창이 표시됩니다.", 24, 51, 650));
        var row = new AppPanel { Location = new Point(24, 92), Size = new Size(1142, 90) };
        row.Controls.Add(new CircleIcon { Icon = AppIcon.Settings, Accent = Colors.Warning, Location = new Point(18, 20), Size = new Size(48, 48) });
        row.Controls.Add(new Label { Location = new Point(84, 16), Size = new Size(320, 24), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, Text = "ID DIP 스위치 검사" });
        row.Controls.Add(new Label { Location = new Point(84, 45), Size = new Size(650, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "ID_SET1·ID_SET2 물리 상태를 ST-LINK로 읽어 ID 1~4로 표시합니다." });
        row.Controls.Add(new Label { Location = new Point(780, 31), Size = new Size(160, 26), Font = Fonts.Caption, ForeColor = Colors.Warning, TextAlign = ContentAlignment.MiddleCenter, Text = "ST-LINK 필요" });
        var open = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.FolderOpen, Text = "열기", Location = new Point(984, 23), Size = new Size(130, 44) };
        open.Click += (_, _) => { using var form = new DioIdSwitchInspectionForm(_idReader, _session); form.ShowDialog(this); };
        row.Controls.Add(open); card.Controls.Add(row); page.Controls.Add(card); return page;
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        if (!_testRunning) { _closing = true; return; }
        e.Cancel = true; _testCts?.Cancel();
        try
        {
            if (_connections.Get(ChannelDefinitions.Uart4.Key).IsConnected && _activeSlaveId.HasValue)
            {
                _session.Results.DioSafetyStatus = DioSafetyStatus.Restoring;
                var safety = await _loopback.EnsureAllOutputsOffAsync(_activeSlaveId.Value, _lastBaseline, CancellationToken.None);
                _session.Results.DioSafetyStatus = safety.Verified ? DioSafetyStatus.OffVerified : DioSafetyStatus.CommandSent;
            }
        }
        catch { _session.Results.DioSafetyStatus = DioSafetyStatus.Failed; }
        _closing = true; Close();
    }

    private static AppCard Card(int x, int y, int width, string label, string value, AppIcon icon, Color valueColor) => new() { Location = new Point(x, y), Size = new Size(width, 100), CardLabel = label, CardValue = value, Icon = icon, IconColor = valueColor, IconSurfaceColor = Color.FromArgb(22, valueColor), ValueColor = valueColor };
    private static Label Title(string text, int x, int y, int width) => new() { Location = new Point(x, y), Size = new Size(width, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = text };
    private static Label Caption(string text, int x, int y, int width) => new() { Location = new Point(x, y), Size = new Size(width, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = text };
    private static void AddReady(Control parent, int y, string name, string value, bool ok)
    {
        parent.Controls.Add(new CircleIcon { Icon = ok ? AppIcon.Check : AppIcon.Warning, Accent = ok ? Colors.Success : Colors.Warning, Location = new Point(24, y), Size = new Size(36, 36) });
        parent.Controls.Add(new Label { Location = new Point(76, y - 1), Size = new Size(130, 20), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = name });
        parent.Controls.Add(new Label { Location = new Point(76, y + 19), Size = new Size(250, 22), Font = Fonts.ButtonText, ForeColor = ok ? Colors.Success : Colors.TextMuted, Text = value });
    }

    protected override void Dispose(bool disposing) { if (disposing) _testCts?.Dispose(); base.Dispose(disposing); }

    private sealed class CircleIcon : Control
    {
        public AppIcon Icon { get; init; }
        public Color Accent { get; init; } = Colors.Primary;
        public CircleIcon() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            GraphicsHelper.SetHighQuality(e.Graphics); using var brush = new SolidBrush(Color.FromArgb(22, Accent)); e.Graphics.FillEllipse(brush, ClientRectangle);
            IconPainter.Draw(e.Graphics, Icon, Rectangle.Inflate(ClientRectangle, -Width / 4, -Height / 4), Accent, 1.7f);
        }
    }
}
