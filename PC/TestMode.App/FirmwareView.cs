namespace TestMode.App;

internal enum FirmwareDisplay { Idle, Uploading, Verifying, Latest, Success, Failed }

// Shared screen for bootloader entry and TEST firmware update.
internal sealed class FirmwareView : UserControl
{
    private readonly Label selection;
    private readonly Label result;
    private readonly Label step;
    private readonly ProgressBar progress;
    private readonly ListBox files;
    private readonly Button fileButton;
    private readonly Button folderButton;
    private readonly Button updateButton;
    private readonly Button checkButton;
    private readonly MainFirmwareUpdateService? updater;
    private readonly DioFirmwareCheckService? dioChecker;
    private readonly bool mainTarget;
    private readonly bool allowUpdate;
    private readonly bool testMode;
    public string? SelectedPath { get; private set; }

    public FirmwareView(bool testMode, bool mainTarget, UsbTestClient usb)
    {
        this.testMode = testMode;
        this.mainTarget = mainTarget;
        allowUpdate = mainTarget && !testMode;
        if (mainTarget) updater = new MainFirmwareUpdateService();
        if (testMode && !mainTarget) dioChecker = new DioFirmwareCheckService(usb);
        Dock = DockStyle.Fill;
        var card = new Surface { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 34f, 30f, 50f, 55f, 92f, 52f, 32f, 28f })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Ui.Label(testMode ? "펌웨어 확인" : "펌웨어 업데이트", Ui.Bold), 0, 0);
        layout.Controls.Add(Ui.Label(testMode
            ? "선택한 HEX와 보드에 기록된 펌웨어가 같은지 확인합니다."
            : "HEX 파일을 선택하고 업데이트를 시작하세요.", color: Ui.Muted), 0, 1);
        fileButton = Ui.Button("chooseHex", "HEX 파일 선택", ChooseFile);
        folderButton = Ui.Button("chooseFolder", "폴더 선택", ChooseFolder);
        var fileActions = Ui.Row(fileButton, folderButton);
        fileActions.Margin = Padding.Empty;
        layout.Controls.Add(fileActions, 0, 2);
        selection = Ui.Label("선택한 파일이 없습니다.", color: Ui.Muted);
        selection.AutoSize = false;
        selection.AutoEllipsis = true;
        selection.Dock = DockStyle.Fill;
        selection.Name = "selectedHex";
        layout.Controls.Add(selection, 0, 3);
        files = new ListBox
        {
            Name = "hexCandidates", Dock = DockStyle.Fill, Font = Ui.Body, IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle, ForeColor = Ui.Ink, HorizontalScrollbar = true,
            DisplayMember = nameof(FileInfo.Name), Margin = new Padding(0, 0, 0, 14), AccessibleName = "폴더의 HEX 파일 목록"
        };
        files.SelectedIndexChanged += (_, _) => { if (files.SelectedItem is FileInfo file) SelectFile(file.FullName); };
        layout.Controls.Add(files, 0, 4);
        updateButton = Ui.Button("updateFirmware", "업데이트 시작", primary: true);
        updateButton.Enabled = false;
        updateButton.Visible = allowUpdate;
        updateButton.Width = 200;
        updateButton.Click += async (_, _) => await StartUpdateAsync();
        layout.Controls.Add(updateButton, 0, 5);
        checkButton = Ui.Button("checkFirmware", "현재 펌웨어 확인", primary: true);
        checkButton.Enabled = false;
        checkButton.Visible = testMode;
        checkButton.Width = 200;
        checkButton.Click += async (_, _) => await CheckFirmwareAsync();
        layout.Controls.Add(checkButton, 0, 6);
        step = Ui.Label("장치 연결 대기", color: Ui.Muted);
        step.Name = "updateStep";
        layout.Controls.Add(step, 0, 7);
        progress = new ProgressBar { Name = "updateProgress", Dock = DockStyle.Fill, Height = 18, Margin = new Padding(0, 0, 0, 8) };
        layout.Controls.Add(progress, 0, 8);
        result = Ui.Label("", Ui.Bold);
        result.Name = "updateResult";
        result.AutoSize = false;
        result.Dock = DockStyle.Fill;
        layout.Controls.Add(result, 0, 9);
        if (updater is not null)
        {
            updater.ProgressChanged += value => RunOnUi(() => progress.Value = Math.Clamp(value, 0, 100));
            updater.StatusChanged += message => RunOnUi(() => step.Text = message);
        }
        card.Controls.Add(layout);
        Controls.Add(card);
    }

    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog { Title = "펌웨어 HEX 선택", Filter = "Intel HEX 파일 (*.hex)|*.hex", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            files.Items.Clear();
            SelectFile(dialog.FileName);
        }
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "HEX 파일이 있는 폴더를 선택하세요.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) SelectFolder(dialog.SelectedPath);
    }

    internal void SelectFolder(string path)
    {
        files.Items.Clear();
        SelectedPath = null;
        SetDisplay(FirmwareDisplay.Idle);
        try
        {
            var candidates = new DirectoryInfo(path).GetFiles().Where(f => f.Extension.Equals(".hex", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            files.Items.AddRange(candidates);
            selection.Text = candidates.Length == 0 ? "선택한 폴더에 HEX 파일이 없습니다." : $"HEX 파일 {candidates.Length}개 · 목록에서 파일을 선택하세요.";
            if (candidates.Length == 1) files.SelectedIndex = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            selection.Text = "폴더를 열 수 없습니다. 경로와 접근 권한을 확인하세요.";
        }
    }

    internal void SelectFile(string path)
    {
        SelectedPath = null;
        SetDisplay(FirmwareDisplay.Idle);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || !file.Extension.Equals(".hex", StringComparison.OrdinalIgnoreCase))
            {
                selection.Text = "사용할 수 있는 HEX 파일을 선택하세요.";
                return;
            }
            SelectedPath = file.FullName;
            selection.Text = $"{file.Name}  ·  {file.Length:N0} bytes\n{file.DirectoryName}";
            updateButton.Enabled = allowUpdate;
            checkButton.Enabled = testMode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            selection.Text = "파일을 열 수 없습니다. 경로와 접근 권한을 확인하세요.";
        }
    }

    internal void SetDisplay(FirmwareDisplay state)
    {
        var busy = state is FirmwareDisplay.Uploading or FirmwareDisplay.Verifying;
        fileButton.Enabled = folderButton.Enabled = files.Enabled = !busy;
        // Firmware transfer remains disabled until its dedicated update stage.
        updateButton.Enabled = allowUpdate && state is FirmwareDisplay.Idle && SelectedPath is not null;
        checkButton.Enabled = testMode && state is FirmwareDisplay.Idle && SelectedPath is not null;
        progress.Value = state switch { FirmwareDisplay.Uploading => 48, FirmwareDisplay.Verifying => 92, FirmwareDisplay.Success => 100, _ => 0 };
        step.Text = state switch
        {
            FirmwareDisplay.Uploading => "펌웨어 기록 중 · 48%",
            FirmwareDisplay.Verifying => "기록 내용 검증 중 · 92%",
            FirmwareDisplay.Latest => "확인 완료",
            FirmwareDisplay.Success => "완료",
            FirmwareDisplay.Failed => testMode ? "펌웨어 확인 실패" : "업데이트 실패",
            _ => "장치 연결 대기"
        };
        result.Text = state switch
        {
            FirmwareDisplay.Latest => "최신 버전입니다.",
            FirmwareDisplay.Success => "펌웨어 업데이트 성공",
            FirmwareDisplay.Failed => "장치 응답이 없습니다. 연결을 확인하고 다시 시도하세요.",
            _ => ""
        };
        result.ForeColor = state == FirmwareDisplay.Failed ? Ui.Red : state == FirmwareDisplay.Success ? Ui.Green : Ui.Blue;
    }

    private async Task StartUpdateAsync()
    {
        if (!allowUpdate || updater is null || SelectedPath is null) return;
        var path = SelectedPath;
        updateButton.Enabled = false;
        fileButton.Enabled = folderButton.Enabled = files.Enabled = false;
        progress.Value = 0;
        step.Text = "부트로더 연결 중";
        result.Text = "";
        try
        {
            var updateResult = await updater.RunAsync(path, CancellationToken.None);
            SetDisplay(updateResult == FirmwareUpdateResult.Latest ? FirmwareDisplay.Latest : FirmwareDisplay.Success);
        }
        catch (OperationCanceledException)
        {
            SetDisplay(FirmwareDisplay.Failed);
        }
        catch (Exception ex)
        {
            result.Text = ex.Message;
            result.ForeColor = Ui.Red;
            step.Text = "업데이트 실패";
            progress.Value = 0;
            fileButton.Enabled = folderButton.Enabled = files.Enabled = true;
        }
    }

    private async Task CheckFirmwareAsync()
    {
        if (!testMode || SelectedPath is null) return;
        var path = SelectedPath;
        checkButton.Enabled = false;
        fileButton.Enabled = folderButton.Enabled = files.Enabled = false;
        progress.Value = 0;
        step.Text = "부트로더 연결 중";
        result.Text = "";
        try
        {
            var checkResult = mainTarget
                ? await updater!.VerifyAsync(path, CancellationToken.None)
                : await dioChecker!.VerifyAsync(path, CancellationToken.None);
            SetDisplay(checkResult == FirmwareCheckResult.Latest ? FirmwareDisplay.Latest : FirmwareDisplay.Failed);
            if (checkResult == FirmwareCheckResult.Different)
            {
                result.Text = "선택한 펌웨어와 다릅니다.";
                result.ForeColor = Ui.Red;
                step.Text = "펌웨어 불일치";
            }
            else
            {
                result.Text = "최신 버전입니다.";
                result.ForeColor = Ui.Green;
                step.Text = "확인 완료";
            }
        }
        catch (Exception ex)
        {
            result.Text = ex.Message;
            result.ForeColor = Ui.Red;
            step.Text = "펌웨어 상태 확인 실패";
            fileButton.Enabled = folderButton.Enabled = files.Enabled = true;
        }
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }
}
