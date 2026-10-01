using System.Drawing;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>UI-03/UI-10 요약 카드 패턴: 원형 아이콘 배경 + 라벨 + 강조 수치.</summary>
public class AppCard : AppPanel
{
    private readonly Label _label;
    private readonly Label _value;
    private AppIcon _icon = AppIcon.Info;
    private Color _iconSurface = Colors.SurfaceBlue;
    private Color _iconColor = Colors.Primary;

    public AppIcon Icon
    {
        get => _icon;
        set { _icon = value; Invalidate(); }
    }

    public Color IconSurfaceColor
    {
        get => _iconSurface;
        set { _iconSurface = value; Invalidate(); }
    }

    public Color IconColor
    {
        get => _iconColor;
        set { _iconColor = value; Invalidate(); }
    }

    private Color? _statusDotColor;

    /// <summary>우측 상단 준비상태 점(작은 원). null이면 표시하지 않는다.</summary>
    public Color? StatusDotColor
    {
        get => _statusDotColor;
        set { _statusDotColor = value; OnResize(EventArgs.Empty); Invalidate(); }
    }

    public string CardLabel
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public string CardValue
    {
        get => _value.Text;
        set => _value.Text = value;
    }

    public Color ValueColor
    {
        get => _value.ForeColor;
        set => _value.ForeColor = value;
    }

    /// <summary>카드 폭이 좁을 때(예: 4분할 준비 상태 카드) 값 문구가 잘리지 않도록 호출부에서 축소 지정할 수 있다.</summary>
    public Font ValueFont
    {
        get => _value.Font;
        set => _value.Font = value;
    }

    /// <summary>라벨 폰트도 값과 동일하게 호출부에서 축소 지정할 수 있다.</summary>
    public Font LabelFont
    {
        get => _label.Font;
        set => _label.Font = value;
    }

    public AppCard()
    {
        Size = new Size(330, 140);
        Padding = new Padding(20);

        _label = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Font = Fonts.Body,
            ForeColor = Colors.TextSecondary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(72, 22),
            Size = new Size(Width - 92, 22)
        };
        _value = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Font = Fonts.MetricValue,
            ForeColor = Colors.TextPrimary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(72, 46),
            Size = new Size(Width - 92, 30)
        };
        Controls.Add(_label);
        Controls.Add(_value);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (_label == null) return;
        int reserve = StatusDotColor.HasValue ? 112 : 92;
        _label.Location = new Point(72, 22);
        _label.Width = Width - reserve;
        _value.Location = new Point(72, 46);
        _value.Width = Width - reserve;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        GraphicsHelper.SetHighQuality(g);

        var circle = new Rectangle(20, 20, 40, 40);
        using (var b = new SolidBrush(_iconSurface)) g.FillEllipse(b, circle);
        var iconRect = new Rectangle(circle.X + 10, circle.Y + 10, 20, 20);
        IconPainter.Draw(g, _icon, iconRect, _iconColor, 1.8f);

        if (StatusDotColor.HasValue)
        {
            using var db = new SolidBrush(StatusDotColor.Value);
            g.FillEllipse(db, Width - 34, 20, 10, 10);
        }
    }
}
