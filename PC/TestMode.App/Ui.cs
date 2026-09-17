using System.Drawing.Drawing2D;

namespace TestMode.App;

internal static class Ui
{
    public static readonly Color Background = ColorTranslator.FromHtml("#F4F7FB");
    public static readonly Color Ink = ColorTranslator.FromHtml("#14243A");
    public static readonly Color Muted = ColorTranslator.FromHtml("#6B7C91");
    public static readonly Color Border = ColorTranslator.FromHtml("#DCE5F0");
    public static readonly Color Blue = ColorTranslator.FromHtml("#246BFD");
    public static readonly Color Red = ColorTranslator.FromHtml("#EE625C");
    public static readonly Color Teal = ColorTranslator.FromHtml("#139A9A");
    public static readonly Color Orange = ColorTranslator.FromHtml("#E68A2E");
    public static readonly Color Green = ColorTranslator.FromHtml("#159A68");
    public static readonly Color PaleBlue = ColorTranslator.FromHtml("#EAF1FF");
    public static readonly Color PaleRed = ColorTranslator.FromHtml("#FFF0EF");
    public static readonly Color PaleTeal = ColorTranslator.FromHtml("#E8F8F7");
    public static readonly Color PaleOrange = ColorTranslator.FromHtml("#FFF3E5");
    public static readonly Color PaleGreen = ColorTranslator.FromHtml("#EAF8F1");
    public static readonly Font Body = new("Segoe UI", 10.5f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Bold = new("Segoe UI", 10.5f, FontStyle.Bold);
    public static readonly Font Heading = new("Segoe UI", 22f, FontStyle.Bold);
    public static readonly Font Metric = new("Segoe UI", 34f, FontStyle.Bold);
    public static readonly Font Logo = new("Segoe UI", 20f, FontStyle.Bold);
    public static readonly Font CardHeading = new("Segoe UI", 21f, FontStyle.Bold);
    public static readonly Font CardIcon = new("Segoe UI", 30f, FontStyle.Bold);

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text, Font = font ?? Body, ForeColor = color ?? Ink,
        AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 12)
    };

    public static Button Button(string name, string text, Action? click = null, bool primary = false, Color? accent = null)
    {
        var color = accent ?? Blue;
        var button = new RoundedButton
        {
            Name = name, Text = text, AutoSize = false, Size = new Size(172, 44),
            Font = Bold, Cursor = Cursors.Hand, Accent = color, Primary = primary,
            BackColor = primary ? color : Color.White, ForeColor = primary ? Color.White : color,
            Margin = new Padding(0, 0, 12, 0),
            AccessibleName = text
        };
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? (primary ? color : Color.White) : Color.FromArgb(235, 239, 245);
            button.ForeColor = button.Enabled ? (primary ? Color.White : color) : Muted;
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
            button.Invalidate();
        };
        if (click is not null) button.Click += (_, _) => click();
        return button;
    }

    public static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Dock = DockStyle.Top, Margin = Padding.Empty, Padding = Padding.Empty
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16)
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    public static TableLayoutPanel Table(int columns, params float[] widths)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = columns, RowCount = 1, Dock = DockStyle.Fill,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent
        };
        foreach (var width in widths) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        return table;
    }

    public static Label Note(string text) => new()
    {
        Text = text, AutoSize = false, Height = 56, Dock = DockStyle.Top,
        Font = Small, ForeColor = Muted, BackColor = PaleBlue,
        Padding = new Padding(16, 10, 16, 8), Margin = new Padding(0, 12, 0, 0)
    };
}

internal sealed class Surface : Panel
{
    public Color Edge { get; set; } = Ui.Border;
    public Surface()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        Padding = new Padding(26);
        Margin = new Padding(0, 0, 16, 0);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Edge, 1.2f);
        using var brush = new SolidBrush(BackColor);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        const int d = 24;
        if (r.Width < d || r.Height < d) return;
        using var path = RoundedPath(r, d);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using var path = RoundedPath(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), 24);
        Region = new Region(path);
    }

    internal static GraphicsPath RoundedPath(Rectangle bounds, int diameter)
    {
        var path = new GraphicsPath();
        var d = Math.Min(diameter, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedButton : Button
{
    public Color Accent { get; set; } = Ui.Blue;
    public bool Primary { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Surface.RoundedPath(bounds, 14);
        var fill = !Enabled ? Color.FromArgb(235, 239, 245) : Primary
            ? (ClientRectangle.Contains(PointToClient(Cursor.Position)) ? ControlPaint.Light(Accent, .05f) : Accent)
            : (ClientRectangle.Contains(PointToClient(Cursor.Position)) ? Ui.PaleBlue : Color.White);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(Enabled && Primary ? Accent : Ui.Border, 1.2f);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        var textColor = !Enabled ? Ui.Muted : Primary ? Color.White : Accent;
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal enum Board { Main, Dio }
internal enum Page { Home, Boards, Bootloader, Switch, Fault, Firmware }
