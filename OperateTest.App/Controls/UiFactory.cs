using OperateTest.App.Theme;

namespace OperateTest.App.Controls;

public static class UiFactory
{
    public static Panel PageCanvas()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.Background,
            AutoScroll = true,
            Padding = new Padding(32, 12, 32, 32)
        };
    }

    public static RoundedPanel Card(string title, string description, int height = 150)
    {
        var card = new RoundedPanel { Height = height, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16) };
        card.Controls.Add(new Label
        {
            Location = new Point(24, 20), Size = new Size(760, 30),
            Font = AppFonts.SectionTitle, ForeColor = AppColors.Text, Text = title
        });
        card.Controls.Add(new Label
        {
            Location = new Point(24, 53), Size = new Size(900, 42),
            Font = AppFonts.Body, ForeColor = AppColors.SubText, Text = description
        });
        return card;
    }

    public static RoundedPanel MetricCard(string caption, string value, Color color)
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        card.Controls.Add(new Label
        {
            Location = new Point(22, 17), Size = new Size(190, 21),
            Font = AppFonts.Small, ForeColor = AppColors.SubText, Text = caption
        });
        card.Controls.Add(new Label
        {
            Location = new Point(22, 43), Size = new Size(240, 34),
            Font = AppFonts.CardTitle, ForeColor = color, Text = value
        });
        return card;
    }

    public static Label TableHeader(string text) => new()
    {
        Dock = DockStyle.Fill, Text = text, Font = AppFonts.BodyBold,
        ForeColor = AppColors.SubText, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0)
    };

    public static Label TableValue(string text, Color? color = null, Font? font = null) => new()
    {
        Dock = DockStyle.Fill, Text = text, Font = font ?? AppFonts.Body,
        ForeColor = color ?? AppColors.Text, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0), AutoEllipsis = true
    };
}
