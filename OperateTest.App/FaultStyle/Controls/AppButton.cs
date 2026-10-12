using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

public enum AppButtonVariant
{
    Primary,
    Warning,
    Secondary,
    Danger,
    OutlinePrimary,
    Icon
}

/// <summary>COMPONENT_INTERACTION_RULES.md 1절 버튼 유형/상태 규칙을 owner-draw로 구현한 버튼.</summary>
public class AppButton : Control
{
    private bool _isPressed;
    private bool _isHover;
    private VisualState? _forcedState;

    public AppButtonVariant Variant { get; set; } = AppButtonVariant.Primary;

    /// <summary>텍스트 좌측에 표시할 벡터 아이콘. null이면 아이콘 없이 텍스트만 표시한다.</summary>
    public AppIcon? IconGlyph { get; set; }

    /// <summary>갤러리에서 상태를 강제로 고정해 보여주기 위한 오버라이드. null이면 실제 마우스/포커스 상태를 사용한다.</summary>
    public VisualState? ForcedState
    {
        get => _forcedState;
        set { _forcedState = value; Invalidate(); }
    }

    public AppButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                  ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Size = new Size(140, Metrics.ButtonHeight);
        BackColor = Colors.Surface;
        Font = Fonts.ButtonText;
    }

    private VisualState EffectiveState()
    {
        if (_forcedState.HasValue) return _forcedState.Value;
        if (!Enabled) return VisualState.Disabled;
        if (_isPressed) return VisualState.Pressed;
        if (_isHover) return VisualState.Hover;
        if (Focused) return VisualState.Focused;
        return VisualState.Normal;
    }

    protected override void OnMouseEnter(EventArgs e) { _isHover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _isHover = false; _isPressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _isPressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _isPressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    private (Color fill, Color border, Color text) Palette(VisualState state)
    {
        return Variant switch
        {
            AppButtonVariant.Primary => state switch
            {
                VisualState.Disabled => (Colors.PrimaryDisabled, Colors.PrimaryDisabled, Color.White),
                VisualState.Pressed => (Colors.PrimaryPressed, Colors.PrimaryPressed, Color.White),
                VisualState.Hover => (Colors.PrimaryHover, Colors.PrimaryHover, Color.White),
                _ => (Colors.Primary, Colors.Primary, Color.White)
            },
            AppButtonVariant.Warning => state switch
            {
                VisualState.Disabled => (Colors.WarningSurface, Colors.WarningSurface, Colors.TextMuted),
                VisualState.Pressed => (ControlPaint.Dark(Colors.Warning, 0.15f), ControlPaint.Dark(Colors.Warning, 0.15f), Color.White),
                VisualState.Hover => (ControlPaint.Light(Colors.Warning, 0.05f), Colors.Warning, Color.White),
                _ => (Colors.Warning, Colors.Warning, Color.White)
            },
            AppButtonVariant.Danger => state switch
            {
                VisualState.Disabled => (Colors.DangerSurface, Colors.DangerSurface, Colors.TextMuted),
                VisualState.Pressed => (ControlPaint.Dark(Colors.Danger, 0.15f), ControlPaint.Dark(Colors.Danger, 0.15f), Color.White),
                VisualState.Hover => (ControlPaint.Light(Colors.Danger, 0.05f), Colors.Danger, Color.White),
                _ => (Colors.Danger, Colors.Danger, Color.White)
            },
            AppButtonVariant.Secondary => state switch
            {
                VisualState.Disabled => (Colors.Surface, Colors.Divider, Colors.TextMuted),
                VisualState.Pressed => (Colors.Divider, Colors.BorderStrong, Colors.TextPrimary),
                VisualState.Hover => (Colors.SurfaceBlue, Colors.BorderStrong, Colors.TextPrimary),
                _ => (Colors.Surface, Colors.Border, Colors.TextPrimary)
            },
            AppButtonVariant.OutlinePrimary => state switch
            {
                VisualState.Disabled => (Colors.Surface, Colors.Divider, Colors.TextMuted),
                VisualState.Pressed => (Colors.SurfaceBlueStrong, Colors.PrimaryPressed, Colors.PrimaryPressed),
                VisualState.Hover => (Colors.SurfaceBlue, Colors.Primary, Colors.Primary),
                _ => (Colors.Surface, Colors.Primary, Colors.Primary)
            },
            AppButtonVariant.Icon => state switch
            {
                VisualState.Disabled => (Color.Transparent, Color.Transparent, Colors.TextMuted),
                VisualState.Pressed => (Colors.SurfaceBlueStrong, Color.Transparent, Colors.Primary),
                VisualState.Hover => (Colors.SurfaceBlue, Color.Transparent, Colors.Primary),
                _ => (Color.Transparent, Color.Transparent, Colors.TextSecondary)
            },
            _ => (Colors.Primary, Colors.Primary, Color.White)
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        GraphicsHelper.SetHighQuality(g);
        g.Clear(Parent?.BackColor ?? Colors.AppBackground);
        var state = EffectiveState();
        var (fill, border, text) = Palette(state);

        // Keep a single anti-aliasing margin. A larger bottom margin made filled
        // buttons look clipped, especially on high-DPI displays.
        var rect = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = GraphicsHelper.RoundedRect(rect, Metrics.ButtonRadius);

        if (fill != Color.Transparent)
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
        }

        if (Variant != AppButtonVariant.Icon)
        {
            using var p = new Pen(border, Variant is AppButtonVariant.OutlinePrimary or AppButtonVariant.Secondary ? 1.5f : 1f);
            g.DrawPath(p, path);
        }

        if (state == VisualState.Focused)
        {
            // A solid inner ring is easier to read than the dotted GDI focus cue and
            // does not look like a broken border on danger buttons.
            var focusRect = Rectangle.Inflate(rect, -4, -4);
            var focusColor = Variant is AppButtonVariant.Primary or AppButtonVariant.Warning or AppButtonVariant.Danger
                ? Color.FromArgb(210, Color.White)
                : Colors.Primary;
            using var fp = new Pen(focusColor, 1.5f) { DashStyle = DashStyle.Solid };
            using var focusPath = GraphicsHelper.RoundedRect(focusRect, Metrics.ButtonRadius - 1);
            g.DrawPath(fp, focusPath);
        }

        if (IconGlyph.HasValue)
        {
            var textSize = TextRenderer.MeasureText(g, Text, Font, rect.Size, TextFormatFlags.NoPrefix);
            const int iconSize = 16;
            const int iconGap = 8;
            int contentWidth = iconSize + iconGap + textSize.Width;
            int startX = rect.X + (rect.Width - contentWidth) / 2;
            var iconRect = new Rectangle(startX, rect.Y + (rect.Height - iconSize) / 2, iconSize, iconSize);
            IconPainter.Draw(g, IconGlyph.Value, iconRect, text, 1.6f);
            var textRect = new Rectangle(startX + iconSize + iconGap, rect.Y, textSize.Width + 4, rect.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            TextRenderer.DrawText(g, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
