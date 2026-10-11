using OperateTest.App.Models;
using OperateTest.App.Services;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class NextBoardSerialForm : Form
{
    private readonly InspectionSession _previous;
    private readonly AppTextBox _serial = new();
    private readonly AppButton _start = new();

    public NextBoardSerialForm(InspectionSession previous)
    {
        _previous = previous;
        Text = "같은 기준서로 다음 보드 검사";
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(680, 315);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        Controls.Add(new Label
        {
            Location = new Point(28, 20), Size = new Size(620, 35), Font = Fonts.PageTitle,
            ForeColor = Colors.TextPrimary, Text = "다음 보드 검사"
        });
        Controls.Add(new Label
        {
            Location = new Point(30, 68), Size = new Size(620, 56), Font = Fonts.Body,
            ForeColor = Colors.TextSecondary, AutoEllipsis = true,
            Text = $"기준서: {Path.GetFileName(previous.TemplatePath)}\n결과 파일: {Path.GetFileName(previous.ResultPath)}"
        });
        Controls.Add(new Label
        {
            Location = new Point(30, 137), Size = new Size(610, 23), Font = Fonts.ButtonText,
            ForeColor = Colors.TextPrimary, Text = "새 보드 시리얼번호"
        });
        _serial.SetBounds(30, 166, 620, 40);
        Controls.Add(_serial);
        Controls.Add(new Label
        {
            Location = new Point(30, 213), Size = new Size(620, 22), Font = Fonts.Caption,
            ForeColor = Colors.TextSecondary,
            Text = "보드 종류·검사일자·검사자·저장 폴더를 유지하고 결과 파일의 다음 빈 행에 기록합니다."
        });

        var cancel = new AppButton
        {
            Variant = AppButtonVariant.Secondary, Text = "취소",
            Location = new Point(358, 253), Size = new Size(132, 44)
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _start.Variant = previous.BoardType == InspectionBoardType.Communication
            ? AppButtonVariant.Primary : AppButtonVariant.Warning;
        _start.Text = "검사 시작";
        _start.SetBounds(506, 253, 144, 44);
        _start.Click += (_, _) => StartNextBoard();
        Controls.Add(cancel);
        Controls.Add(_start);
        Shown += (_, _) => _serial.Focus();
    }

    public InspectionSession? Session { get; private set; }

    private void StartNextBoard()
    {
        _serial.HasError = string.IsNullOrWhiteSpace(_serial.TextValue);
        if (_serial.HasError)
        {
            MessageBox.Show(this, "새 보드의 시리얼번호를 입력하세요.", "검사 정보 확인",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _start.Enabled = false;
        try
        {
            var workbooks = new InspectionWorkbookService();
            var session = workbooks.CreateSession(
                _previous.TemplatePath,
                Path.GetDirectoryName(_previous.ResultPath)!,
                _previous.BoardType,
                _previous.InspectionDate,
                _previous.Inspector,
                _serial.TextValue);
            new InspectionCheckpointService().Save(session);
            Session = session;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "다음 보드 준비 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _start.Enabled = true;
        }
    }
}
