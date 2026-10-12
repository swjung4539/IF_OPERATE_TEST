using OperateTest.App.Controls;
using OperateTest.App.Models;
using OperateTest.App.Services;
using OperateTest.App.Services.CommInspection;
using OperateTest.App.Services.StLink;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class CommInspectionForm : InspectionShellForm
{
    private readonly ISerialConnectionManager _connections;
    private readonly IInspectionProtocolServices _protocols;
    private readonly ICommRotaryReader _rotaryReader;
    private readonly ICommInspectionService _inspection;
    private readonly ICommEmulationHost _keepAliveHost;
    private readonly InspectionSession _session;
    private readonly List<ConnectionRow> _connectionRows = [];
    private readonly Queue<string> _recentCommLogs = [];
    private CancellationTokenSource? _testCts;
    private bool _testRunning;
    private bool _closing;
    private bool _closeCleanupRunning;

    public CommInspectionForm(ISerialConnectionManager connections, IInspectionProtocolServices protocols, ICommRotaryReader rotaryReader, InspectionSession session)
        : base("COMM 보드 검사", "통신 경로 · 로터리 스위치 · FAULT 접점", Colors.Primary, session: session)
    {
        _connections = connections;
        _protocols = protocols;
        _rotaryReader = rotaryReader;
        _session = session;
        _inspection = new CommInspectionService(connections, protocols);
        _keepAliveHost = new CommEmulationHost(connections, protocols);
        ConfigureConnectionReadiness(() =>
        {
            var connected = ChannelDefinitions.CommPortChannels.Count(channel => _connections.Get(channel.Key).IsConnected);
            return (connected == ChannelDefinitions.CommPortChannels.Count, $"COMM 포트 {connected}/4");
        });
        Shown += (_, _) => Navigate("dashboard");
        FormClosing += OnClosing;
    }

    protected override void Navigate(string key)
    {
        if (_testRunning && key != "test")
        {
            MessageBox.Show(this, "통신 검사를 먼저 중지하세요.", "통신 검사", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        switch (key)
        {
            case "result":
                ShowPage(key, "최종 판정", "필수 검사 완료 여부를 확인하고 제품의 최종 PASS/FAIL을 확정합니다.", new InspectionSummaryControl(_session, RequestContinuation, RefreshWorkflow));
                break;
            case "settings":
                ShowPage(key, "COMM 통신 설정", "각 경로의 COM 포트와 통신 속도·UART 형식을 설정합니다.", BuildSettingsPage());
                break;
            case "test":
                ShowPage(key, "COMM 통신 검사", "검사할 경로를 선택하고 반복 조건을 설정한 뒤 실제 요청·응답을 확인합니다.", BuildTestPage());
                break;
            case "manual":
                ShowPage(key, "육안 및 전원 검사", "작업자가 직접 확인한 육안 상태와 전원 측정 결과를 입력합니다.", new ManualInspectionControl(_session, RefreshWorkflow));
                break;
            case "fault-switch":
                ShowPage(key, "FAULT 및 스위치 검사", "항목의 열기 버튼을 누르면 전용 검사창이 표시됩니다.", BuildToolsPage());
                break;
            default:
                ShowPage("dashboard", "COMM 보드 검사 대시보드", "통신 연결 상태와 검사 준비 항목을 한 화면에서 확인합니다.", BuildDashboard());
                break;
        }
        UpdateTopStatus();
    }

    private Panel BasePage() => new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        AutoScrollMinSize = new Size(1252, 710),
        BackColor = Colors.AppBackground
    };

    private Control BuildDashboard()
    {
        var page = BasePage();
        var connected = ChannelDefinitions.CommPortChannels.Count(x => _connections.Get(x.Key).IsConnected);
        var uart4Connected = _connections.Get(ChannelDefinitions.Uart4.Key).IsConnected;
        var cardWidth = 286;
        page.Controls.Add(SummaryCard(30, 12, cardWidth, "COMM 포트", $"{connected} / 4 연결", AppIcon.Keyboard, connected == 4 ? Colors.Success : Colors.TextMuted));
        page.Controls.Add(SummaryCard(332, 12, cardWidth, "통신 검사 경로", "4개", AppIcon.Network, Colors.Primary));
        page.Controls.Add(SummaryCard(634, 12, cardWidth, "스위치 검사", "로터리 0~F", AppIcon.Settings, Colors.Primary));
        page.Controls.Add(SummaryCard(936, 12, cardWidth, "FAULT 검사", "TB18 접점", AppIcon.Shield, Colors.Danger));

        var topology = new AppPanel { Location = new Point(30, 136), Size = new Size(790, 280) };
        topology.Controls.Add(Title("시스템 통신 구성 및 상태", 24, 20, 420));
        topology.Controls.Add(Caption("COMM MAIN과 각 통신 경로의 검사 구성을 표시합니다.", 24, 50, 600));
        var names = new[] { ("PC", AppIcon.Monitor), ("MAIN", AppIcon.Cpu), ("Heating", AppIcon.Thermometer), ("Gas", AppIcon.Flame), ("Backup", AppIcon.Shield) };
        for (var i = 0; i < names.Length; i++)
        {
            var x = 24 + i * 146;
            topology.Controls.Add(new CircleIcon { Icon = names[i].Item2, Location = new Point(x + 21, 105), Size = new Size(54, 54), Accent = Colors.Primary });
            topology.Controls.Add(new Label { Location = new Point(x, 169), Size = new Size(96, 23), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, TextAlign = ContentAlignment.MiddleCenter, Text = names[i].Item1 });
            var channelConnected = i switch
            {
                0 => true,
                1 => uart4Connected,
                2 => _connections.Get(ChannelDefinitions.Heating.Key).IsConnected,
                3 => _connections.Get(ChannelDefinitions.Gas.Key).IsConnected,
                4 => _connections.Get(ChannelDefinitions.Backup.Key).IsConnected,
                _ => false
            };
            var responseKey = i switch
            {
                1 => ChannelDefinitions.Uart4.Key,
                2 => ChannelDefinitions.Heating.Key,
                3 => ChannelDefinitions.Gas.Key,
                4 => ChannelDefinitions.Backup.Key,
                _ => string.Empty
            };
            var response = string.IsNullOrEmpty(responseKey)
                ? new DeviceResponseState { Status = DeviceResponseStatus.Responding }
                : DeviceResponseTracker.Get(_session.Results, responseKey);
            var nodeText = i == 0
                ? "검사 프로그램"
                : !channelConnected
                    ? "포트 미연결"
                    : DeviceResponseTracker.StatusText(response);
            topology.Controls.Add(new Label { Location = new Point(x, 201), Size = new Size(96, 38), Font = Fonts.Caption, ForeColor = ResponseColor(response.Status, channelConnected), TextAlign = ContentAlignment.TopCenter, Text = nodeText, AutoEllipsis = true });
            if (i < names.Length - 1) topology.Controls.Add(new Panel { Location = new Point(x + 87, 131), Size = new Size(58, 1), BackColor = Colors.BorderStrong });
        }
        page.Controls.Add(topology);

        var recent = new AppPanel { Location = new Point(842, 136), Size = new Size(380, 280) };
        recent.Controls.Add(Title("검사 준비 상태", 24, 20, 300));
        AddReadyRow(recent, 72, "COMM 포트", connected == 4 ? "4/4 연결됨" : $"{connected}/4 연결", connected == 4);
        AddReadyRow(recent, 122, "ST-LINK", _rotaryReader.CliPath is null ? "CLI 없음" : "CLI 확인", _rotaryReader.CliPath is not null);
        AddReadyRow(recent, 172, "로터리 검사", "전용 창에서 실행", true);
        var heatingReady = _connections.Get(ChannelDefinitions.Heating.Key).IsConnected;
        AddReadyRow(recent, 222, "FAULT 접점", heatingReady ? "Heating 준비됨" : "Heating 연결 필요", heatingReady);
        page.Controls.Add(recent);

        var quick = new AppPanel { Location = new Point(30, 438), Size = new Size(1192, 186) };
        quick.Controls.Add(Title("빠른 실행", 24, 20, 300));
        quick.Controls.Add(Caption("육안·전원 확인 후 통신과 기능검사를 순서대로 진행하세요.", 24, 50, 650));
        var manual = new AppButton { Variant = AppButtonVariant.Primary, IconGlyph = AppIcon.Clipboard, Text = "육안·전원 검사", Location = new Point(24, 99), Size = new Size(220, 48) };
        var settings = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Settings, Text = "통신 설정", Location = new Point(258, 99), Size = new Size(220, 48) };
        var test = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Play, Text = "통신 검사", Location = new Point(492, 99), Size = new Size(220, 48) };
        var tool = new AppButton { Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.Settings, Text = "FAULT 및 스위치", Location = new Point(726, 99), Size = new Size(250, 48) };
        test.Enabled = connected == ChannelDefinitions.CommPortChannels.Count;
        test.AccessibleDescription = test.Enabled ? "통신 검사 실행 화면 열기" : "COMM 포트 4개를 모두 연결한 후 사용할 수 있습니다.";
        manual.Click += (_, _) => Navigate("manual"); settings.Click += (_, _) => Navigate("settings"); test.Click += (_, _) => Navigate("test"); tool.Click += (_, _) => Navigate("fault-switch");
        quick.Controls.Add(manual); quick.Controls.Add(settings); quick.Controls.Add(test); quick.Controls.Add(tool);
        page.Controls.Add(quick);
        return page;
    }

    private Control BuildSettingsPage()
    {
        _connectionRows.Clear();
        var page = BasePage();
        var main = new AppPanel { Location = new Point(30, 12), Size = new Size(1192, 500) };
        main.Controls.Add(Title("1. COMM 포트 연결", 24, 20, 450));
        main.Controls.Add(Caption("MAIN UART4, Heating Jacket, Gas Detector, Backup GST 포트를 각각 설정합니다. DIO는 별도 PC 포트를 사용하지 않습니다.", 24, 51, 1050));
        var refresh = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Refresh, Text = "포트 새로고침", Location = new Point(994, 20), Size = new Size(168, 40) };
        refresh.Click += (_, _) => { foreach (var row in _connectionRows) row.RefreshPorts(); };
        main.Controls.Add(refresh);

        var header = new Panel { Location = new Point(24, 88), Size = new Size(1138, 34), BackColor = Colors.SurfaceBlue };
        header.Controls.Add(Caption("통신 경로", 12, 7, 210));
        header.Controls.Add(Caption("COM Port", 240, 7, 150));
        header.Controls.Add(Caption("Baud rate", 420, 7, 120));
        header.Controls.Add(Caption("UART format", 560, 7, 120));
        header.Controls.Add(Caption("연결", 706, 7, 110));
        header.Controls.Add(Caption("상태", 870, 7, 180));
        main.Controls.Add(header);
        var y = 124;
        foreach (var definition in ChannelDefinitions.CommPortChannels)
        {
            var row = new ConnectionRow(_connections, _protocols, definition) { Location = new Point(24, y), Size = new Size(1138, 66) };
            row.ConnectionStateChanged += (_, snapshot) =>
            {
                DeviceResponseTracker.Update(_session.Results, definition.Key,
                    snapshot.State == SerialConnectionState.Connected
                        ? DeviceResponseStatus.Responding : DeviceResponseStatus.NotChecked,
                    snapshot.State == SerialConnectionState.Connected
                        ? "포트 연결 시 장치 응답 확인 완료" : "포트 연결 후 응답 확인 필요");
                UpdateTopStatus();
                RefreshWorkflow();
            };
            _connectionRows.Add(row);
            main.Controls.Add(row);
            y += 68;
        }
        page.Controls.Add(main);

        var guide = new AppPanel { Location = new Point(30, 532), Size = new Size(1192, 126) };
        guide.Controls.Add(Title("연결 기준", 24, 18, 250));
        guide.Controls.Add(Caption("기본값: MAIN 115200 8-N-1 · Heating 19200 8-N-1 · Gas 9600 8-E-1 · Backup 115200 8-N-1", 24, 54, 1080));
        guide.Controls.Add(Caption("포트 연결은 실제 장치 요청·응답이 확인된 경우에만 성공합니다. DIO는 COMM 통신 검사에서 제외합니다.", 24, 78, 1080));
        page.Controls.Add(guide);
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
        var forever = new AppToggleSwitch { Location = new Point(220, 41), IsOn = false };
        repeatCard.Controls.Add(repeatInput);
        repeatCard.Controls.Add(new Label { Location = new Point(145, 39), Size = new Size(45, 30), Font = Fonts.Body, ForeColor = Colors.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Text = "회" });
        repeatCard.Controls.Add(forever);
        repeatCard.Controls.Add(new Label { Location = new Point(284, 39), Size = new Size(100, 28), Font = Fonts.Body, ForeColor = Colors.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, Text = "계속 반복" });
        repeatCard.Controls.Add(new Label { Location = new Point(402, 18), Size = new Size(225, 52), Font = Fonts.Caption, ForeColor = Colors.TextMuted, Text = "계속 반복 ON 시\nSTOP까지 반복합니다." });
        page.Controls.Add(repeatCard);

        var metricY = 116;
        var totalCard = SummaryCard(30, metricY, 280, "TOTAL", "0", AppIcon.Clipboard, Colors.TextPrimary);
        var passCard = SummaryCard(326, metricY, 280, "PASS", "0", AppIcon.Shield, Colors.Success);
        var failCard = SummaryCard(622, metricY, 280, "FAIL", "0", AppIcon.Warning, Colors.Danger);
        var errorCard = SummaryCard(918, metricY, 280, "ERROR", "0", AppIcon.Info, Colors.Warning);
        page.Controls.Add(totalCard); page.Controls.Add(passCard); page.Controls.Add(failCard); page.Controls.Add(errorCard);

        var list = new AppPanel { Location = new Point(30, 240), Size = new Size(760, 434) };
        list.Controls.Add(Title("검사 항목", 20, 16, 200));
        list.Controls.Add(Caption("검사할 통신 경로만 ON으로 설정합니다.", 20, 45, 420));
        var tableHeader = new Panel { Location = new Point(18, 78), Size = new Size(724, 34), BackColor = Colors.SurfaceBlue };
        string[] headers = { "검사 경로", "ON/OFF", "T", "P", "F", "E", "판정" };
        int[] hx = { 12, 330, 420, 468, 516, 564, 620 };
        int[] hw = { 300, 76, 40, 40, 40, 40, 88 };
        for (var i = 0; i < headers.Length; i++) tableHeader.Controls.Add(Caption(headers[i], hx[i], 7, hw[i]));
        list.Controls.Add(tableHeader);

        var toggles = new Dictionary<string, AppToggleSwitch>();
        var verdicts = new Dictionary<string, StatusBadge>();
        var counts = new Dictionary<string, int[]>();
        var countLabels = new Dictionary<string, Label[]>();
        var details = new Dictionary<string, Label>();
        var y = 114;
        foreach (var definition in ChannelDefinitions.CommChannels)
        {
            var row = new Panel { Location = new Point(18, y), Size = new Size(724, 58), BackColor = Colors.Surface };
            row.Controls.Add(new Label { Location = new Point(12, 8), Size = new Size(300, 21), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, Text = definition.DisplayName });
            var detail = new Label { Location = new Point(12, 31), Size = new Size(300, 19), Font = Fonts.Caption, ForeColor = Colors.TextMuted, Text = definition.InterfaceName, AutoEllipsis = true };
            details[definition.Key] = detail; row.Controls.Add(detail);
            var toggle = new AppToggleSwitch { Location = new Point(338, 16), IsOn = true };
            toggles[definition.Key] = toggle; row.Controls.Add(toggle);
            var labels = new Label[4];
            for (var i = 0; i < 4; i++)
            {
                labels[i] = new Label { Location = new Point(414 + i * 48, 14), Size = new Size(42, 30), Font = Fonts.Body, ForeColor = i == 1 ? Colors.Success : i == 2 ? Colors.Danger : i == 3 ? Colors.Warning : Colors.TextPrimary, TextAlign = ContentAlignment.MiddleCenter, Text = "0" };
                row.Controls.Add(labels[i]);
            }
            countLabels[definition.Key] = labels; counts[definition.Key] = new int[4];
            var badge = new StatusBadge { Location = new Point(620, 16), Status = AppStatus.NotRun };
            verdicts[definition.Key] = badge; row.Controls.Add(badge);
            list.Controls.Add(row); y += 60;
        }
        page.Controls.Add(list);

        var resultCard = new AppPanel { Location = new Point(810, 240), Size = new Size(388, 434) };
        resultCard.Controls.Add(Title("최근 검사 결과", 20, 16, 250));
        resultCard.Controls.Add(Caption("경로별 마지막 응답 내용을 표시합니다.", 20, 45, 330));
        var latest = new TextBox { Location = new Point(20, 82), Size = new Size(348, 280), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, BackColor = Colors.Surface, BorderStyle = BorderStyle.None, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = _recentCommLogs.Count == 0 ? "표시할 검사 결과가 없습니다." : string.Join(Environment.NewLine + Environment.NewLine, _recentCommLogs.Reverse()) };
        resultCard.Controls.Add(latest);
        var runStatus = new Label { Location = new Point(20, 372), Size = new Size(348, 42), Font = Fonts.ButtonText, ForeColor = Colors.TextMuted, Text = "검사 대기", TextAlign = ContentAlignment.MiddleLeft };
        resultCard.Controls.Add(runStatus);
        page.Controls.Add(resultCard);

        var total = 0; var passed = 0; var failed = 0; var errors = 0;
        void UpdateMetrics()
        {
            totalCard.CardValue = total.ToString(); passCard.CardValue = passed.ToString(); failCard.CardValue = failed.ToString(); errorCard.CardValue = errors.ToString();
        }
        void ResetResults()
        {
            total = passed = failed = errors = 0; _recentCommLogs.Clear(); UpdateMetrics(); latest.Text = "표시할 검사 결과가 없습니다."; runStatus.Text = "검사 대기"; runStatus.ForeColor = Colors.TextMuted;
            _session.Results.InvalidateFinalization();
            _session.Results.CommunicationPassed = null;
            _session.Results.CommunicationStatus = InspectionExecutionStatus.NotRun;
            foreach (var key in ChannelDefinitions.CommChannels.Select(channel => channel.Key))
                _session.Results.DeviceResponses.Remove(key);
            foreach (var key in counts.Keys.ToArray())
            {
                Array.Clear(counts[key]); foreach (var label in countLabels[key]) label.Text = "0";
                verdicts[key].Status = AppStatus.NotRun; details[key].Text = ChannelDefinitions.CommChannels.First(x => x.Key == key).InterfaceName;
            }
        }

        stop.Click += (_, _) => _testCts?.Cancel();
        reset.Click += (_, _) => ResetResults();
        start.Click += async (_, _) =>
        {
            if (_testRunning) return;
            var selected = toggles.Where(x => x.Value.IsOn).Select(x => x.Key).ToArray();
            if (selected.Length == 0) { MessageBox.Show(this, "검사할 통신 경로를 하나 이상 ON으로 설정하세요.", "통신 검사", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var missingConnections = GetMissingRequiredConnections(selected);
            if (missingConnections.Length > 0)
            {
                var missingText = string.Join(Environment.NewLine,
                    missingConnections.Select(definition => $"• {definition.DisplayName}"));
                MessageBox.Show(this,
                    $"선택한 검사에 필요한 통신 포트가 연결되지 않았습니다.{Environment.NewLine}{Environment.NewLine}{missingText}{Environment.NewLine}{Environment.NewLine}[통신 설정]에서 먼저 연결하세요.",
                    "통신 연결 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!int.TryParse(repeatInput.TextValue, out var repeat) || repeat < 1)
            {
                repeatInput.HasError = true; MessageBox.Show(this, "반복 횟수는 1 이상의 숫자여야 합니다.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
            }
            repeatInput.HasError = false;
            _session.Results.InvalidateFinalization();
            _testCts?.Dispose(); _testCts = new CancellationTokenSource(); _testRunning = true;
            _session.Results.CommunicationStatus = InspectionExecutionStatus.Running;
            foreach (var key in selected)
                DeviceResponseTracker.Update(_session.Results, key, DeviceResponseStatus.Checking, "장치 응답 확인 중");
            RefreshWorkflow();
            start.Enabled = reset.Enabled = false; stop.Enabled = true; repeatInput.Enabled = forever.Enabled = false;
            foreach (var toggle in toggles.Values) toggle.Enabled = false;
            var round = 0;
            var runFailed = 0;
            var audit = new InspectionAuditService();
            var attempt = audit.BeginAttempt(_session, "communication", $"경로 {selected.Length}개, 반복 {repeat}회");
            Exception? attemptException = null;
            IReadOnlyList<CommPathResult> lastRoundResults = [];
            try
            {
                await SafetyOperationService.RunWithTimeoutAsync(
                    "COMM 정상 통신 유지 종료",
                    SafetyOperationService.FaultRestoreTimeout,
                    ct => _keepAliveHost.StopAsync(ct: ct));
                while (!_testCts.IsCancellationRequested && (forever.IsOn || round < repeat))
                {
                    round++; runStatus.Text = forever.IsOn ? $"반복 {round}회 실행 중" : $"{round}/{repeat}회 실행 중"; runStatus.ForeColor = Colors.Primary;
                    foreach (var key in selected) verdicts[key].Status = AppStatus.Running;
                    var progress = new Progress<CommPathResult>(result =>
                    {
                        details[result.ChannelKey].Text = result.Detail;
                        if (result.Verdict == InspectionVerdict.Running)
                        {
                            DeviceResponseTracker.Update(_session.Results, result.ChannelKey, DeviceResponseStatus.Checking, result.Detail);
                            return;
                        }
                        DeviceResponseTracker.Update(
                            _session.Results,
                            result.ChannelKey,
                            result.Verdict == InspectionVerdict.Pass
                                ? DeviceResponseStatus.Responding
                                : DeviceResponseTracker.ClassifyFailure(result.Detail),
                            result.Detail);
                        _recentCommLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {result.DisplayName} · {result.Verdict}{Environment.NewLine}{result.Detail}");
                        while (_recentCommLogs.Count > 10) _recentCommLogs.Dequeue();
                        latest.Text = string.Join(Environment.NewLine + Environment.NewLine, _recentCommLogs.Reverse());
                        latest.SelectionStart = 0;
                        latest.ScrollToCaret();
                        var c = counts[result.ChannelKey]; c[0]++; total++;
                        if (result.Verdict == InspectionVerdict.Pass) { c[1]++; passed++; verdicts[result.ChannelKey].Status = AppStatus.Pass; }
                        else { c[2]++; failed++; runFailed++; verdicts[result.ChannelKey].Status = AppStatus.Fail; }
                        for (var i = 0; i < 4; i++) countLabels[result.ChannelKey][i].Text = c[i].ToString();
                        UpdateMetrics();
                    });
                    lastRoundResults = await _inspection.RunSelectedAsync(selected, progress, _testCts.Token);
                }
                _testCts.Token.ThrowIfCancellationRequested();
                if (!_testCts.IsCancellationRequested
                    && selected.Length == ChannelDefinitions.CommChannels.Count
                    && lastRoundResults.Count == selected.Length
                    && lastRoundResults.All(result => result.Verdict == InspectionVerdict.Pass))
                {
                    runStatus.Text = "검사 완료 · 정상 통신 유지 준비 중";
                    await _keepAliveHost.PrepareNormalAsync(_testCts.Token);
                    runStatus.Text = $"검사 완료 · PASS · {round}회 · 정상 통신 유지 중";
                    runStatus.ForeColor = Colors.Success;
                }
                else
                {
                    runStatus.Text = $"검사 완료 · {(runFailed == 0 && errors == 0 ? "PASS" : "FAIL")} · {round}회";
                    runStatus.ForeColor = runFailed == 0 && errors == 0 ? Colors.Success : Colors.Danger;
                }
                if (selected.Length == ChannelDefinitions.CommChannels.Count)
                {
                    _session.Results.CommunicationPassed = runFailed == 0 && errors == 0
                        && lastRoundResults.Count == selected.Length
                        && lastRoundResults.All(result => result.Verdict == InspectionVerdict.Pass);
                    _session.Results.CommunicationStatus = _session.Results.CommunicationPassed == true
                        ? InspectionExecutionStatus.Pass
                        : InspectionExecutionStatus.Fail;
                }
                else
                {
                    _session.Results.CommunicationPassed = null;
                    _session.Results.CommunicationStatus = InspectionExecutionStatus.Stopped;
                }
            }
            catch (OperationCanceledException)
            {
                _session.Results.CommunicationPassed = null;
                _session.Results.CommunicationStatus = InspectionExecutionStatus.Stopped;
                foreach (var key in selected.Where(key => verdicts[key].Status == AppStatus.Running))
                {
                    verdicts[key].Status = AppStatus.Stopped;
                    DeviceResponseTracker.Update(_session.Results, key, DeviceResponseStatus.NotChecked, "검사 중단으로 응답 확인 미완료");
                }
                runStatus.Text = "검사 중단 · 판정 없음";
                runStatus.ForeColor = Colors.TextMuted;
            }
            catch (Exception ex)
            {
                attemptException = ex;
                _session.Results.CommunicationPassed = null;
                _session.Results.CommunicationStatus = InspectionExecutionStatus.Error;
                foreach (var key in selected.Where(key => verdicts[key].Status == AppStatus.Running))
                {
                    verdicts[key].Status = AppStatus.ErrorState;
                    DeviceResponseTracker.Update(_session.Results, key, DeviceResponseTracker.ClassifyFailure(ex.Message), ex.Message);
                }
                errors++; total++; UpdateMetrics(); runStatus.Text = $"검사 종료 · 오류: {ex.Message}"; runStatus.ForeColor = Colors.Warning;
            }
            finally
            {
                audit.CompleteAttempt(_session, "communication", attempt, _session.Results.CommunicationStatus, runStatus.Text, attemptException);
                new InspectionCheckpointService().TrySave(_session);
                _testRunning = false; start.Enabled = reset.Enabled = true; stop.Enabled = false; repeatInput.Enabled = forever.Enabled = true;
                foreach (var toggle in toggles.Values) toggle.Enabled = true;
                UpdateTopStatus();
                RefreshWorkflow();
            }
        };
        return page;
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (_closeCleanupRunning) return;
        _closeCleanupRunning = true;
        _testCts?.Cancel();
        Exception? cleanupError = null;
        try
        {
            await SafetyOperationService.RunWithTimeoutAsync(
                "COMM 통신 안전 종료",
                SafetyOperationService.FaultRestoreTimeout,
                ct => _keepAliveHost.StopAsync(ct: ct));
            new InspectionAuditService().TryRecord(
                _session, "safety_cleanup_completed", "communication", "success", "COMM 통신 작업 및 정상 통신 유지 종료 완료");
        }
        catch (Exception ex)
        {
            cleanupError = ex;
            _session.Results.CommunicationStatus = InspectionExecutionStatus.Error;
            new InspectionAuditService().TryRecord(
                _session, "safety_cleanup_failed", "communication", "error",
                $"COMM 통신 종료 실패: {ex.Message}", ex);
            new InspectionCheckpointService().TrySave(_session);
        }

        _closing = true;
        if (cleanupError is not null)
        {
            MessageBox.Show(this,
                $"COMM 통신을 정상 종료하지 못했습니다.{Environment.NewLine}{Environment.NewLine}" +
                $"{cleanupError.Message}{Environment.NewLine}{Environment.NewLine}" +
                "보드 전원과 통신 케이블 상태를 확인한 뒤 다시 검사하세요.",
                "안전 종료 확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Close();
    }

    private Control BuildToolsPage()
    {
        var page = BasePage();
        var card = new AppPanel { Location = new Point(30, 12), Size = new Size(1192, 330) };
        card.Controls.Add(Title("검사 항목", 24, 20, 300));
        card.Controls.Add(Caption("열기 버튼을 누르면 해당 항목의 전용 검사창이 표시됩니다.", 24, 51, 650));
        var stLinkReady = _rotaryReader.CliPath is not null;
        var heatingReady = _connections.Get(ChannelDefinitions.Heating.Key).IsConnected;
        AddToolRow(card, 90, AppIcon.Settings, "로터리 스위치 검사", "ST-LINK로 PC0~PC3을 읽어 0~F 위치를 실시간 표시합니다.", stLinkReady ? "ST-LINK CLI 확인" : "STM32 Programmer 설치 필요", stLinkReady, () => { using var form = new RotaryInspectionForm(_rotaryReader, _session); form.ShowDialog(this); RefreshWorkflow(); });
        AddToolRow(card, 190, AppIcon.Shield, "FAULT 접점 검사", "Heating ID20 응답 차단·복구 후 TB18 개방/도통을 검사자가 확인합니다.", heatingReady ? "Heating 포트 준비됨" : "Heating 포트 연결 필요", heatingReady, OpenFaultInspection);
        page.Controls.Add(card);
        return page;
    }


    private void OpenFaultInspection()
    {
        if (!_connections.Get(ChannelDefinitions.Heating.Key).IsConnected)
        {
            MessageBox.Show(this,
                "FAULT OPEN/CLOSE 유도에 필요한 Heating Jacket 포트를 [통신 설정]에서 연결하세요.",
                "통신 연결 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var form = new FaultInspectionForm(_connections, _protocols, _keepAliveHost, _session);
        form.ShowDialog(this);
        RefreshWorkflow();
    }

    private SerialChannelDefinition[] GetMissingRequiredConnections(IEnumerable<string> selectedKeys)
    {
        var connectionKeys = selectedKeys.Distinct(StringComparer.OrdinalIgnoreCase);

        return connectionKeys
            .Select(key => ChannelDefinitions.CommPortChannels.First(definition => definition.Key == key))
            .Where(definition => !_connections.Get(definition.Key).IsConnected)
            .ToArray();
    }
    private static AppCard SummaryCard(int x, int y, int width, string label, string value, AppIcon icon, Color valueColor) => new()
    {
        Location = new Point(x, y), Size = new Size(width, 100), CardLabel = label, CardValue = value,
        Icon = icon, IconColor = valueColor, IconSurfaceColor = Color.FromArgb(22, valueColor), ValueColor = valueColor
    };

    private static Label Title(string text, int x, int y, int width) => new() { Location = new Point(x, y), Size = new Size(width, 28), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = text };
    private static Label Caption(string text, int x, int y, int width) => new() { Location = new Point(x, y), Size = new Size(width, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = text };

    private static void AddReadyRow(Control parent, int y, string name, string value, bool ok)
    {
        parent.Controls.Add(new CircleIcon { Icon = ok ? AppIcon.Check : AppIcon.Warning, Accent = ok ? Colors.Success : Colors.Warning, Location = new Point(24, y), Size = new Size(34, 34) });
        parent.Controls.Add(new Label { Location = new Point(72, y - 1), Size = new Size(145, 21), Font = Fonts.Body, ForeColor = Colors.TextSecondary, Text = name });
        parent.Controls.Add(new Label { Location = new Point(72, y + 20), Size = new Size(230, 22), Font = Fonts.ButtonText, ForeColor = ok ? Colors.Success : Colors.TextMuted, Text = value });
    }

    private static void AddToolRow(Control parent, int y, AppIcon icon, string title, string description, string requirement, bool enabled, Action open)
    {
        var row = new AppPanel { Location = new Point(24, y), Size = new Size(1142, 84), BackColor = Colors.Surface };
        row.Controls.Add(new CircleIcon { Icon = icon, Accent = Colors.Primary, Location = new Point(18, 18), Size = new Size(46, 46) });
        row.Controls.Add(new Label { Location = new Point(82, 14), Size = new Size(310, 24), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, Text = title });
        row.Controls.Add(new Label { Location = new Point(82, 42), Size = new Size(640, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = description });
        row.Controls.Add(new Label { Location = new Point(744, 20), Size = new Size(220, 44), Font = Fonts.Caption, ForeColor = enabled ? Colors.Success : Colors.Warning, TextAlign = ContentAlignment.MiddleCenter, Text = requirement });
        var button = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.FolderOpen, Text = "열기", Location = new Point(984, 20), Size = new Size(130, 44) };
        button.Enabled = enabled;
        button.AccessibleDescription = enabled ? $"{title} 열기" : requirement;
        button.Click += (_, _) => open(); row.Controls.Add(button); parent.Controls.Add(row);
    }

    private void UpdateTopStatus()
    {
        var connected = ChannelDefinitions.CommPortChannels.Count(x => _connections.Get(x.Key).IsConnected);
        var responding = ChannelDefinitions.CommChannels.Count(channel =>
        {
            return _connections.Get(channel.Key).IsConnected
                   && DeviceResponseTracker.Get(_session.Results, channel.Key).Status == DeviceResponseStatus.Responding;
        });
        var text = connected < 4
            ? $"포트 {connected}/4 연결 · 장치 응답 {responding}/4"
            : responding == 4
                ? "포트 4/4 연결 · 장치 응답 4/4 정상"
                : $"포트 4/4 연결 · 장치 응답 {responding}/4 확인";
        SetTopStatus(text, responding == 4 ? Colors.Success : connected == 4 ? Colors.Primary : Colors.TextMuted);
    }

    private static Color ResponseColor(DeviceResponseStatus status, bool portAvailable) => !portAvailable
        ? Colors.TextMuted
        : status switch
        {
            DeviceResponseStatus.Responding => Colors.Success,
            DeviceResponseStatus.NoResponse => Colors.Danger,
            DeviceResponseStatus.ProtocolError => Colors.Warning,
            DeviceResponseStatus.Checking => Colors.Primary,
            _ => Colors.TextMuted
        };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _testCts?.Dispose();
        base.Dispose(disposing);
    }

    private sealed class CircleIcon : Control
    {
        public AppIcon Icon { get; init; }
        public Color Accent { get; init; } = Colors.Primary;
        public CircleIcon() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            GraphicsHelper.SetHighQuality(e.Graphics);
            using var brush = new SolidBrush(Color.FromArgb(22, Accent));
            e.Graphics.FillEllipse(brush, ClientRectangle);
            var icon = Rectangle.Inflate(ClientRectangle, -Width / 4, -Height / 4);
            IconPainter.Draw(e.Graphics, Icon, icon, Accent, 1.7f);
        }
    }
}
