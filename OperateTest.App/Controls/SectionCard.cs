using OperateTest.App.Theme;

namespace OperateTest.App.Controls;

public sealed class SectionCard : RoundedPanel
{
    public SectionCard(string title, string description)
    {
        Height = 126;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 12);

        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(20, 20),
            Size = new Size(Width - 40, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = AppFonts.SectionTitle,
            ForeColor = AppColors.Text,
            Text = title
        });
        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(20, 54),
            Size = new Size(Width - 40, 46),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = AppFonts.Body,
            ForeColor = AppColors.SubText,
            Text = description
        });
    }
}
