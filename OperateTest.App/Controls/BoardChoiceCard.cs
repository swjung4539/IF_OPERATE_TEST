using OperateTest.App.Theme;

namespace OperateTest.App.Controls;

public sealed class BoardChoiceCard : RoundedPanel
{
    public BoardChoiceCard(string title, string description, string buttonText, Color accent)
    {
        Size = new Size(560, 390);
        Padding = new Padding(26);
        BorderColor = Color.FromArgb(170, accent);
        BorderWidth = 1.4f;

        var icon = new Label
        {
            Location = new Point(26, 24), Size = new Size(76, 76), BackColor = Color.FromArgb(22, accent),
            ForeColor = accent, Font = new Font("맑은 고딕", 26, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter, Text = title.StartsWith("COMM") ? "⇄" : "▦"
        };
        var titleLabel = new Label
        {
            Location = new Point(124, 28), Size = new Size(390, 34),
            Font = new Font("맑은 고딕", 17, FontStyle.Bold), ForeColor = accent, Text = title
        };
        var desc = new Label
        {
            Location = new Point(124, 65), Size = new Size(390, 54),
            Font = AppFonts.Body, ForeColor = AppColors.SubText, Text = description
        };
        Controls.Add(icon); Controls.Add(titleLabel); Controls.Add(desc);
        Controls.Add(new Panel { Location = new Point(26, 134), Size = new Size(508, 1), BackColor = AppColors.Divider });

        var features = title.StartsWith("COMM")
            ? new[] { ("통신", "5개 경로"), ("스위치", "로터리 0~F"), ("FAULT", "TB18 접점") }
            : new[] { ("통신", "RS-485"), ("루프백", "DO ↔ DI"), ("스위치", "ID 1~4") };
        for (var i = 0; i < features.Length; i++)
        {
            var tile = new RoundedPanel
            {
                Location = new Point(26 + i * 174, 156), Size = new Size(160, 114),
                BackColor = AppColors.Surface, BorderColor = AppColors.Border
            };
            tile.Controls.Add(new Label { Location = new Point(10, 22), Size = new Size(140, 28), Font = AppFonts.BodyBold, ForeColor = accent, TextAlign = ContentAlignment.MiddleCenter, Text = features[i].Item1 });
            tile.Controls.Add(new Label { Location = new Point(10, 58), Size = new Size(140, 28), Font = AppFonts.Small, ForeColor = AppColors.Text, TextAlign = ContentAlignment.MiddleCenter, Text = features[i].Item2 });
            Controls.Add(tile);
        }

        var open = new StyledButton
        {
            Location = new Point(26, 302), Size = new Size(508, 54), Text = $"›  {buttonText}",
            Font = new Font("맑은 고딕", 12, FontStyle.Bold), BackColor = accent,
            FlatAppearance = { BorderColor = accent, MouseOverBackColor = ControlPaint.Dark(accent, 0.08f) }
        };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(open);
    }

    public event EventHandler? OpenRequested;
}
