using OperateTest.App.Models;
using OperateTest.App.Services;
using TestEmulator.Controls;
using TestEmulator.Layout;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class NextBoardSetupForm : Form
{
    private readonly InspectionSession _completedSession;
    private readonly InspectionWorkbookService _workbooks = new();
    private readonly AppTextBox _serial = new();
    private readonly AppButton _start = new();

    public NextBoardSetupForm(InspectionSession completedSession, WorkbookUsage usage)
    {
        _completedSession = completedSession;
        var boardName = completedSession.BoardType == InspectionBoardType.Communication ? "COMM" : "DIO";

        Text = $"{boardName} 다음 보드 검사";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ResponsiveWindow.Configure(this, new Size(650, 390), new Size(600, 360), scrollWhenCompact: true);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        Controls.Add(new Label
        {
            Location = new Point(30, 24), Size = new Size(590, 36),
            Font = Fonts.PageTitle, ForeColor = Colors.TextPrimary,
            Text = $"같은 문서에 {boardName} 보드 추가"
        });
        Controls.Add(new Label
        {
            Location = new Point(32, 64), Size = new Size(580, 23), Font = Fonts.Body,
            ForeColor = Colors.TextSecondary, Text = "검사자·날짜·기준서·결과 파일은 유지하고 새 시리얼만 입력합니다."
        });

        var card = new AppPanel { Location = new Point(30, 108), Size = new Size(590, 194) };
        card.Controls.Add(new Label
        {
            Location = new Point(22, 18), Size = new Size(100, 22), Font = Fonts.Caption,
            ForeColor = Colors.TextSecondary, Text = "현재 문서"
        });
        card.Controls.Add(new Label
        {
            Location = new Point(22, 42), Size = new Size(546, 24), Font = Fonts.Body,
            ForeColor = Colors.TextPrimary, Text = Path.GetFileName(completedSession.ResultPath), AutoEllipsis = true
        });
        card.Controls.Add(new Label
        {
            Location = new Point(22, 74), Size = new Size(546, 22), Font = Fonts.Caption,
            ForeColor = Colors.Primary,
            Text = $"기록 현황 {usage.UsedRows}/{usage.Capacity} · 다음 기록 번호 {usage.UsedRows + 1} · 저장 후 남은 수량 {Math.Max(0, usage.RemainingRows - 1)}"
        });
        card.Controls.Add(new Label
        {
            Location = new Point(22, 112), Size = new Size(160, 22), Font = Fonts.Caption,
            ForeColor = Colors.TextSecondary, Text = "새 시리얼번호"
        });
        _serial.SetBounds(22, 138, 546, 40);
        _serial.EnterPressed += (_, _) => CreateNextSession();
        card.Controls.Add(_serial);
        Controls.Add(card);

        Controls.Add(new Label
        {
            Location = new Point(32, 309), Size = new Size(300, 24), Font = Fonts.Caption,
            ForeColor = Colors.TextSecondary,
            Text = $"입력 예: {InspectionWorkbookService.ExpectedSerialExample(completedSession.BoardType)}"
        });

        var cancel = new AppButton
        {
            Variant = AppButtonVariant.Secondary, Text = "취소",
            Location = new Point(350, 330), Size = new Size(128, 44)
        };
        cancel.Click += (_, _) => Close();
        _start.Variant = completedSession.BoardType == InspectionBoardType.Communication
            ? AppButtonVariant.Primary
            : AppButtonVariant.Warning;
        _start.IconGlyph = AppIcon.ChevronRight;
        _start.Text = "다음 검사 시작";
        _start.SetBounds(492, 330, 128, 44);
        _start.Click += (_, _) => CreateNextSession();
        Controls.Add(cancel);
        Controls.Add(_start);
    }

    public InspectionSession? Session { get; private set; }

    private void CreateNextSession()
    {
        _serial.HasError = string.IsNullOrWhiteSpace(_serial.TextValue);
        if (_serial.HasError)
        {
            MessageBox.Show(this, "새 보드의 시리얼번호를 입력하세요.",
                "시리얼번호 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _start.Enabled = false;
        try
        {
            Session = _workbooks.CreateNextSession(_completedSession, _serial.TextValue);
            new InspectionCheckpointService().Save(Session);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _serial.HasError = true;
            MessageBox.Show(this, ex.Message, "다음 보드 준비 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _start.Enabled = true;
        }
    }
}
