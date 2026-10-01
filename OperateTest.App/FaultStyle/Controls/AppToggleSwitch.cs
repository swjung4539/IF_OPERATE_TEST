using System.Drawing;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>DESIGN_SYSTEM.md 5절 토글 최소 54×26, 반경 13(pill). COMPONENT_INTERACTION_RULES.md 3절 ON/OFF 의미.</summary>
public class AppToggleSwitch : Control
{
    private bool _isOn;
    private bool _isHover;
    private VisualState? _forcedState;

    public bool IsOn
    {
        get => _isOn;
        set { _isOn = value; Invalidate(); IsOnChanged?.Invoke(this, EventArgs.Empty); }
    }

    public event EventHandler? IsOnChanged;

    public VisualState? ForcedState
    {
        get => _forcedState;
        set { _forcedState = value; Invalidate(); }
    }

    public AppToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(Metrics.ToggleWidth, Metrics.ToggleHeight);
    }

    protected override void OnMouseEnter(EventArgs e) { _isHover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _isHover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { if (Enabled) IsOn = !IsOn; base.OnClick(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    private VisualState EffectiveState()
    {
        if (_forcedState.HasValue) return _forcedState.Value;
        if (!Enabled) return VisualState.Disabled;
        if (Focused) return VisualState.Focused;
        if (_isHover) return VisualState.Hover;
        return VisualState.Normal;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        GraphicsHelper.SetHighQuality(g);
        var state = EffectiveState();
        bool on = state == VisualState.Selected || _isOn;

        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        Color trackColor = on
            ? (Enabled ? (state == VisualState.Hover ? Colors.PrimaryHover : Colors.Primary) : Colors.PrimaryDisabled)
            : (Enabled ? Colors.Divider : Colors.AppBackground);

        using var path = GraphicsHelper.RoundedRect(track, Height / 2);
        using (var b = new SolidBrush(trackColor)) g.FillPath(b, path);
        using (var p = new Pen(Colors.Border, 1f)) { if (!on) g.DrawPath(p, path); }

        int knobDia = Height - 6;
        int knobX = on ? Width - knobDia - 3 : 3;
        var knob = new Rectangle(knobX, 3, knobDia, knobDia);
        using var kb = new SolidBrush(Color.White);
        g.FillEllipse(kb, knob);

        if (state == VisualState.Focused)
        {
            using var fp = new Pen(Colors.Primary, 1.5f);
            g.DrawPath(fp, GraphicsHelper.RoundedRect(new Rectangle(-2, -2, Width + 3, Height + 3), Height / 2 + 2));
        }
    }
}
