using System.Drawing;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>DESIGN_SYSTEM.md 6절 좌측 메뉴 행. selected 시 Primary 배경, hover 시 SurfaceBlueStrong.</summary>
public class SideMenuButton : Control
{
    private bool _isHover;
    private bool _isSelected;
    private VisualState? _forcedState;

    public AppIcon Icon { get; set; } = AppIcon.Home;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; Invalidate(); }
    }

    public VisualState? ForcedState
    {
        get => _forcedState;
        set { _forcedState = value; Invalidate(); }
    }

    public event EventHandler? Selected;

    public SideMenuButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(Metrics.SideMenuWidth, Metrics.SideMenuRowHeight);
        Font = Fonts.ButtonText;
    }

    protected override void OnMouseEnter(EventArgs e) { _isHover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _isHover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { IsSelected = true; Selected?.Invoke(this, EventArgs.Empty); base.OnClick(e); }

    private VisualState EffectiveState()
    {
        if (_forcedState.HasValue) return _forcedState.Value;
        if (!Enabled) return VisualState.Disabled;
        if (_isSelected) return VisualState.Selected;
        if (_isHover) return VisualState.Hover;
        return VisualState.Normal;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        GraphicsHelper.SetHighQuality(g);
        var state = EffectiveState();

        Color bg = state switch
        {
            VisualState.Selected => Colors.Primary,
            VisualState.Hover => Colors.SurfaceBlueStrong,
            VisualState.Disabled => Color.Transparent,
            _ => Color.Transparent
        };
        Color fg = state == VisualState.Selected ? Color.White : Colors.TextPrimary;
        if (state == VisualState.Disabled) fg = Colors.TextMuted;

        var rect = new Rectangle(6, 4, Width - 12, Height - 8);
        if (bg != Color.Transparent)
        {
            using var path = GraphicsHelper.RoundedRect(rect, 8);
            using var b = new SolidBrush(bg);
            g.FillPath(b, path);
        }

        var iconRect = new Rectangle(rect.Left + 16, rect.Top + (rect.Height - 22) / 2, 22, 22);
        IconPainter.Draw(g, Icon, iconRect, fg);

        var textRect = new Rectangle(iconRect.Right + 12, rect.Top, rect.Width - (iconRect.Right + 12 - rect.Left) - 8, rect.Height);
        TextRenderer.DrawText(g, Text, Font, textRect, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
