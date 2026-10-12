using System.Drawing;
using System.Windows.Forms;
using OperateTest.App.Services;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

public sealed class WorkflowProgressBar : Control
{
    private IReadOnlyList<WorkflowStepState> _steps = [];

    public IReadOnlyList<WorkflowStepState> Steps => _steps;

    public WorkflowProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        Height = 78;
        MinimumSize = new Size(600, 78);
        BackColor = Color.Transparent;
    }

    public void SetSteps(IReadOnlyList<WorkflowStepState> steps)
    {
        _steps = steps;
        AccessibleName = "검사 진행 단계";
        AccessibleDescription = string.Join(", ", steps.Select(step => $"{step.Name} {StateText(step.State)}"));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        GraphicsHelper.SetHighQuality(e.Graphics);
        if (_steps.Count == 0) return;

        const int circleSize = 24;
        const int circleTop = 2;
        const int lineY = circleTop + circleSize / 2;
        const int horizontalMargin = 14;
        var availableWidth = Math.Max(1, Width - horizontalMargin * 2);
        var columnWidth = Math.Max(96, Width / _steps.Count);
        var centers = _steps.Count == 1
            ? new[] { Width / 2 }
            : Enumerable.Range(0, _steps.Count)
                .Select(index => horizontalMargin + index * availableWidth / (_steps.Count - 1))
                .ToArray();

        // Draw connectors first so the circles cover their ends cleanly.
        for (var index = 0; index < _steps.Count - 1; index++)
        {
            using var line = new Pen(
                _steps[index].State == WorkflowStageState.Complete ? Colors.Success : Colors.BorderStrong,
                2f);
            e.Graphics.DrawLine(line, centers[index], lineY, centers[index + 1], lineY);
        }

        for (var index = 0; index < _steps.Count; index++)
        {
            var step = _steps[index];
            var centerX = centers[index];
            var color = StateColor(step.State);
            var circle = new Rectangle(centerX - circleSize / 2, circleTop, circleSize, circleSize);
            using var fill = new SolidBrush(step.State == WorkflowStageState.Pending ? Colors.Surface : color);
            using var border = new Pen(color, 1.5f);
            e.Graphics.FillEllipse(fill, circle);
            e.Graphics.DrawEllipse(border, circle);
            TextRenderer.DrawText(e.Graphics, (index + 1).ToString(), Fonts.Caption, circle,
                step.State == WorkflowStageState.Pending ? color : Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            var labelLeft = Math.Max(0, centerX - columnWidth / 2);
            var labelWidth = Math.Min(columnWidth, Width - labelLeft);
            TextRenderer.DrawText(e.Graphics, step.Name, Fonts.ButtonText,
                new Rectangle(labelLeft, 31, labelWidth, 22), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, step.Detail, Fonts.Caption,
                new Rectangle(labelLeft, 55, labelWidth, 20), Colors.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private static Color StateColor(WorkflowStageState state) => state switch
    {
        WorkflowStageState.Complete => Colors.Success,
        WorkflowStageState.Current => Colors.Primary,
        WorkflowStageState.Attention => Colors.Warning,
        _ => Colors.TextMuted
    };

    private static string StateText(WorkflowStageState state) => state switch
    {
        WorkflowStageState.Complete => "완료",
        WorkflowStageState.Current => "현재 단계",
        WorkflowStageState.Attention => "확인 필요",
        _ => "대기"
    };
}
