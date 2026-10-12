using System.Diagnostics;
using OperateTest.App.Models;
using TestEmulator.Controls;
using TestEmulator.Layout;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class InspectionCompletionForm : Form
{
    public InspectionCompletionForm(InspectionSession session, WorkbookUsage usage)
    {
        Text = "검사 완료 후 작업 선택";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ResponsiveWindow.Configure(this, new Size(760, 520), new Size(680, 480), scrollWhenCompact: true);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        Controls.Add(new Label
        {
            Location = new Point(30, 24), Size = new Size(700, 38),
            Font = Fonts.PageTitle, ForeColor = Colors.Success,
            Text = "검사 결과 저장 완료"
        });
        Controls.Add(new Label
        {
            Location = new Point(32, 65), Size = new Size(690, 24),
            Font = Fonts.Body, ForeColor = Colors.TextSecondary,
            Text = "다음 작업을 선택하세요. 창을 닫으면 현재 최종 판정 화면에 머무릅니다."
        });

        var info = new AppPanel { Location = new Point(30, 108), Size = new Size(700, 140) };
        info.Controls.Add(Label("현재 결과 파일", 20, 17, 130, Fonts.ButtonText, Colors.TextPrimary));
        info.Controls.Add(new Label
        {
            Location = new Point(20, 45), Size = new Size(660, 24), Font = Fonts.Caption,
            ForeColor = Colors.TextSecondary, Text = Path.GetFileName(session.ResultPath), AutoEllipsis = true
        });
        info.Controls.Add(Label("완료 시리얼", 20, 86, 100, Fonts.Caption, Colors.TextSecondary));
        info.Controls.Add(Label(session.SerialNumber, 122, 83, 180, Fonts.ButtonText, Colors.TextPrimary));
        info.Controls.Add(Label("기록 현황", 360, 86, 85, Fonts.Caption, Colors.TextSecondary));
        info.Controls.Add(Label($"{usage.UsedRows} / {usage.Capacity}  (남은 {usage.RemainingRows}개)", 447, 83, 220, Fonts.ButtonText,
            usage.IsFull ? Colors.Danger : Colors.Primary));
        Controls.Add(info);

        var same = ActionButton(
            session.BoardType == InspectionBoardType.Communication ? AppButtonVariant.Primary : AppButtonVariant.Warning,
            AppIcon.ChevronRight,
            "같은 문서에 다음 보드 검사",
            30,
            270);
        same.Enabled = !usage.IsFull;
        if (usage.IsFull) same.Text = "같은 문서 사용 완료 (30 / 30) · 새 문서를 선택하세요";
        same.Click += (_, _) => SelectAction(InspectionContinuationAction.SameWorkbook);
        Controls.Add(same);

        var newWorkbook = ActionButton(AppButtonVariant.OutlinePrimary, AppIcon.Document, "새 문서로 검사", 30, 334);
        newWorkbook.Click += (_, _) => SelectAction(InspectionContinuationAction.NewWorkbook);
        Controls.Add(newWorkbook);

        var finish = ActionButton(AppButtonVariant.Secondary, AppIcon.Check, "검사 종료 · 보드 선택 화면", 30, 398);
        finish.Click += (_, _) => SelectAction(InspectionContinuationAction.Finish);
        Controls.Add(finish);

        var openExcel = new AppButton
        {
            Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.FolderOpen,
            Text = "Excel 열기", Location = new Point(470, 462), Size = new Size(124, 40)
        };
        var openFolder = new AppButton
        {
            Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.FolderOpen,
            Text = "폴더 열기", Location = new Point(606, 462), Size = new Size(124, 40)
        };
        openExcel.Click += (_, _) => OpenPath(session.ResultPath);
        openFolder.Click += (_, _) => OpenPath(Path.GetDirectoryName(session.ResultPath)!);
        Controls.Add(openExcel);
        Controls.Add(openFolder);

    }

    public InspectionContinuationAction SelectedAction { get; private set; }

    private void SelectAction(InspectionContinuationAction action)
    {
        SelectedAction = action;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static AppButton ActionButton(AppButtonVariant variant, AppIcon icon, string text, int x, int y) => new()
    {
        Variant = variant,
        IconGlyph = icon,
        Text = text,
        Location = new Point(x, y),
        Size = new Size(700, 50),
        Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold)
    };

    private static Label Label(string text, int x, int y, int width, Font font, Color color) => new()
    {
        Location = new Point(x, y), Size = new Size(width, 25),
        Font = font, ForeColor = color, Text = text
    };

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"경로를 열 수 없습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
