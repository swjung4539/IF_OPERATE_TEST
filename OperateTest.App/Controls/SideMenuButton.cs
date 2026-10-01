using OperateTest.App.Theme;

namespace OperateTest.App.Controls;

public sealed class SideMenuButton : Button
{
    private bool _selected;

    public bool Selected
    {
        get => _selected;
        set { _selected = value; ApplyStyle(); }
    }

    public SideMenuButton()
    {
        Height = AppMetrics.SideMenuRowHeight;
        Dock = DockStyle.Top;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(22, 0, 0, 0);
        Font = AppFonts.BodyBold;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        ApplyStyle();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!Selected) BackColor = AppColors.SurfaceBlueStrong;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        ApplyStyle();
    }

    private void ApplyStyle()
    {
        BackColor = Selected ? AppColors.Primary : AppColors.Surface;
        ForeColor = Selected ? Color.White : AppColors.Text;
        FlatAppearance.MouseDownBackColor = Selected ? AppColors.PrimaryHover : AppColors.SurfaceBlueStrong;
    }
}
