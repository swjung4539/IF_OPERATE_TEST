using System.IO.Ports;

namespace FirmwareUpdater;

/// <summary>
/// IF_Controller 펌웨어 업데이트 GUI의 메인 화면. 컨트롤을 전부 코드로
/// 생성한다(별도 .Designer.cs/.resx 없이 이 파일 하나로 완결).
/// </summary>
public sealed class MainForm : Form
{
    private readonly ComboBox _cboPort = new();
    private readonly Button _btnRefreshPort = new() { Text = "새로고침" };

    private readonly TextBox _txtSlotAPath = new() { ReadOnly = true };
    private readonly Button _btnBrowseA = new() { Text = "찾아보기..." };

    private readonly TextBox _txtSlotBPath = new() { ReadOnly = true };
    private readonly Button _btnBrowseB = new() { Text = "찾아보기..." };

    private readonly NumericUpDown _numMajor = new() { Minimum = 0, Maximum = 255, Value = 1 };
    private readonly NumericUpDown _numMinor = new() { Minimum = 0, Maximum = 255, Value = 0 };
    private readonly NumericUpDown _numPatch = new() { Minimum = 0, Maximum = 255, Value = 0 };

    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100 };
    private readonly TextBox _txtLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9F),
    };

    private readonly Button _btnCheckStatus = new() { Text = "연결 확인" };
    private readonly Button _btnVerifyB = new() { Text = "Slot B 검증(읽기 비교)" };
    private readonly Button _btnStart = new() { Text = "업데이트 시작" };

    private CancellationTokenSource? _cts;

    public MainForm()
    {
        Text = "IF_Controller 펌웨어 업데이트";
        Width = 640;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        RefreshPorts();

        _btnRefreshPort.Click += (_, _) => RefreshPorts();
        _btnBrowseA.Click += (_, _) => BrowseFor(_txtSlotAPath, "Slot A 펌웨어(.bin) 선택");
        _btnBrowseB.Click += (_, _) => BrowseFor(_txtSlotBPath, "Slot B 펌웨어(.bin) 선택");
        _btnCheckStatus.Click += async (_, _) => await OnCheckStatusClicked();
        _btnVerifyB.Click += async (_, _) => await OnVerifyBClicked();
        _btnStart.Click += async (_, _) => await OnStartClicked();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(10),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _cboPort.Width = 200;
        layout.Controls.Add(new Label { Text = "COM 포트", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        var portButtonsPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        portButtonsPanel.Controls.Add(_cboPort);
        layout.Controls.Add(portButtonsPanel, 1, 0);
        var portActionsPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        portActionsPanel.Controls.Add(_btnRefreshPort);
        portActionsPanel.Controls.Add(_btnCheckStatus);
        layout.Controls.Add(portActionsPanel, 2, 0);

        layout.Controls.Add(new Label { Text = "Slot A 펌웨어", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _txtSlotAPath.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtSlotAPath, 1, 1);
        layout.Controls.Add(_btnBrowseA, 2, 1);

        layout.Controls.Add(new Label { Text = "Slot B 펌웨어", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _txtSlotBPath.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtSlotBPath, 1, 2);
        layout.Controls.Add(_btnBrowseB, 2, 2);

        var versionPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        versionPanel.Controls.Add(new Label { Text = "버전(Major.Minor.Patch)", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        versionPanel.Controls.Add(_numMajor);
        versionPanel.Controls.Add(new Label { Text = ".", AutoSize = true, Padding = new Padding(4, 6, 4, 0) });
        versionPanel.Controls.Add(_numMinor);
        versionPanel.Controls.Add(new Label { Text = ".", AutoSize = true, Padding = new Padding(4, 6, 4, 0) });
        versionPanel.Controls.Add(_numPatch);
        layout.Controls.Add(versionPanel, 1, 3);

        _btnStart.Width = 140;
        _btnStart.Height = 32;
        layout.Controls.Add(_btnStart, 2, 3);

        layout.Controls.Add(new Label { Text = "진단", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(_btnVerifyB, 2, 4);

        Controls.Add(_txtLog);
        Controls.Add(_progress);
        _progress.Dock = DockStyle.Top;
        _progress.Height = 20;
        Controls.Add(layout);
    }

    private void RefreshPorts()
    {
        string? previouslySelected = _cboPort.SelectedItem as string;
        _cboPort.Items.Clear();
        _cboPort.Items.AddRange(SerialPort.GetPortNames());

        if (previouslySelected != null && _cboPort.Items.Contains(previouslySelected))
        {
            _cboPort.SelectedItem = previouslySelected;
        }
        else if (_cboPort.Items.Count > 0)
        {
            _cboPort.SelectedIndex = 0;
        }
    }

    private void BrowseFor(TextBox target, string title)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "펌웨어 이미지 (*.bin)|*.bin|모든 파일 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.FileName;
        }
    }

    private async Task OnCheckStatusClicked()
    {
        if (_cboPort.SelectedItem is not string portName)
        {
            MessageBox.Show(this, "COM 포트를 선택해 주세요.", "확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _txtLog.Clear();
        SetInputsEnabled(false);

        var service = new UpdateService();
        service.LogMessage += AppendLog;

        _cts = new CancellationTokenSource();
        try
        {
            await service.CheckStatusAsync(portName, _cts.Token);
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
        }
        finally
        {
            SetInputsEnabled(true);
        }
    }

    private async Task OnVerifyBClicked()
    {
        if (_cboPort.SelectedItem is not string portName)
        {
            MessageBox.Show(this, "COM 포트를 선택해 주세요.", "확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrEmpty(_txtSlotBPath.Text))
        {
            MessageBox.Show(this, "Slot B 펌웨어(.bin) 파일을 선택해 주세요(비교 기준 파일).", "확인 필요",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        byte[] expected;
        try
        {
            expected = File.ReadAllBytes(_txtSlotBPath.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"파일을 읽을 수 없습니다: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _txtLog.Clear();
        _progress.Value = 0;
        SetInputsEnabled(false);

        var service = new UpdateService();
        service.LogMessage += AppendLog;
        service.ProgressChanged += p => BeginInvoke(() => _progress.Value = Math.Clamp(p, 0, 100));

        _cts = new CancellationTokenSource();
        try
        {
            await service.VerifyFlashAsync(portName, BootProtocol.SlotBBase, expected, _cts.Token);
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
        }
        finally
        {
            SetInputsEnabled(true);
        }
    }

    private async Task OnStartClicked()
    {
        if (_cboPort.SelectedItem is not string portName)
        {
            MessageBox.Show(this, "COM 포트를 선택해 주세요.", "확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrEmpty(_txtSlotAPath.Text) || string.IsNullOrEmpty(_txtSlotBPath.Text))
        {
            MessageBox.Show(this, "Slot A / Slot B 펌웨어(.bin) 파일을 모두 선택해 주세요.", "확인 필요",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        byte[] slotA, slotB;
        try
        {
            slotA = File.ReadAllBytes(_txtSlotAPath.Text);
            slotB = File.ReadAllBytes(_txtSlotBPath.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"펌웨어 파일을 읽을 수 없습니다: {ex.Message}", "오류",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var version = ((byte)_numMajor.Value, (byte)_numMinor.Value, (byte)_numPatch.Value);

        _txtLog.Clear();
        _progress.Value = 0;
        SetInputsEnabled(false);

        var service = new UpdateService();
        service.LogMessage += AppendLog;
        service.ProgressChanged += p => BeginInvoke(() => _progress.Value = Math.Clamp(p, 0, 100));

        _cts = new CancellationTokenSource();
        try
        {
            await service.RunAsync(portName, slotA, slotB, version, _cts.Token);
            MessageBox.Show(this, "펌웨어 업데이트가 완료되고 장치가 재부팅되었습니다.", "완료",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
            MessageBox.Show(this, $"업데이트 중 오류가 발생했습니다:\n{ex.Message}\n\n" +
                "기존 슬롯은 FINALIZE 이전 실패라면 그대로 유지되므로 장치는 계속 정상 동작합니다.",
                "업데이트 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetInputsEnabled(true);
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        _cboPort.Enabled = enabled;
        _btnRefreshPort.Enabled = enabled;
        _btnCheckStatus.Enabled = enabled;
        _btnVerifyB.Enabled = enabled;
        _btnBrowseA.Enabled = enabled;
        _btnBrowseB.Enabled = enabled;
        _numMajor.Enabled = enabled;
        _numMinor.Enabled = enabled;
        _numPatch.Enabled = enabled;
        _btnStart.Enabled = enabled;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }
        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
