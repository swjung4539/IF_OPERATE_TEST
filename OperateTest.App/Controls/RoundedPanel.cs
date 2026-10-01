using System.Drawing.Drawing2D;
using OperateTest.App.Theme;

namespace OperateTest.App.Controls;

public class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = 12;
    public Color BorderColor { get; set; } = AppColors.Border;
    public float BorderWidth { get; set; } = 1f;

    public RoundedPanel()
    {
        BackColor = AppColors.Surface;
        DoubleBuffered = true;
        Padding = new Padding(20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = Drawing.RoundedRectangle(bounds, CornerRadius);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(BorderColor, BorderWidth);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}
