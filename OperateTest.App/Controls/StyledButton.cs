using OperateTest.App.Theme;

namespace OperateTest.App.Controls;

public sealed class StyledButton : Button
{
    private bool _primary = true;

    public bool Primary
    {
        get => _primary;
        set { _primary = value; ApplyStyle(); }
    }

    public StyledButton()
    {
        Height = 38;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        Cursor = Cursors.Hand;
        Font = AppFonts.BodyBold;
        UseVisualStyleBackColor = false;
        ApplyStyle();
    }

    private void ApplyStyle()
    {
        if (_primary)
        {
            BackColor = AppColors.Primary;
            ForeColor = Color.White;
            FlatAppearance.BorderColor = AppColors.Primary;
            FlatAppearance.MouseOverBackColor = AppColors.PrimaryHover;
        }
        else
        {
            BackColor = AppColors.Surface;
            ForeColor = AppColors.Text;
            FlatAppearance.BorderColor = AppColors.Border;
            FlatAppearance.MouseOverBackColor = AppColors.NeutralSurface;
        }
    }
}
