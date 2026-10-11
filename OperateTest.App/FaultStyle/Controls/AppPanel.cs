using System.Drawing;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>DESIGN_SYSTEM.md 5절 "일반 카드" 기준(반경 8~10, 1px Border)의 둥근 컨테이너.</summary>
public class AppPanel : Panel
{
    private VisualState? _forcedState;
    private int _cornerRadius = Metrics.CardRadius;
    // Colors.Border(#D8E4F2)는 Colors.AppBackground/Colors.Surface(#FFFFFF)와 명도 차가 너무 작아
    // 카드 경계가 거의 안 보였다("박스 구분이 잘 안됨") — 카드 전용으로 조금 더 진한 톤을 쓴다.
    private static readonly Color DefaultCardBorder = ColorTranslator.FromHtml("#A7C0DE");
    private Color _borderColor = DefaultCardBorder;

    public int CornerRadius
    {
        get => _cornerRadius;
        set { _cornerRadius = value; Invalidate(); }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    public VisualState? ForcedState
    {
        get => _forcedState;
        set { _forcedState = value; Invalidate(); }
    }

    public AppPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Colors.Surface;
        Padding = new Padding(Spacing.S5);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        GraphicsHelper.SetHighQuality(g);
        var state = _forcedState ?? (Enabled ? VisualState.Normal : VisualState.Disabled);

        // Keep the full anti-aliased border inside the client area.
        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = GraphicsHelper.RoundedRect(rect, _cornerRadius);
        using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);

        var borderColor = state switch
        {
            VisualState.Error => Colors.Danger,
            VisualState.Selected => Colors.Primary,
            _ => _borderColor
        };
        using var p = new Pen(borderColor, state == VisualState.Selected ? 1.6f : 1.4f);
        g.DrawPath(p, path);
    }
}
