using System.Drawing;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>DESIGN_SYSTEM.md 5절 입력 34~38 높이·5~7 모서리를 따르는 커스텀 테두리 입력창.
/// 네이티브 TextBox는 테두리를 직접 그릴 수 없으므로 BorderStyle.None 내부 TextBox를 감싸는 owner-draw 컨테이너로 구현한다.</summary>
public class AppTextBox : Panel
{
    private readonly TextBox _inner;
    private bool _hasError;
    private VisualState? _forcedState;

    public event EventHandler? TextChanged2;

    /// <summary>Enter 키를 눌렀을 때만 발생 — "값을 입력하고 Enter를 누르면 설정됐다는 걸 알고
    /// 싶다" 같은 화면에서 TextChanged2(키 입력마다 발생)와 구분해서 쓸 수 있게 별도로 둔다.</summary>
    public event EventHandler? EnterPressed;

    public string TextValue
    {
        get => _inner.Text;
        set => _inner.Text = value;
    }

    public bool ReadOnly
    {
        get => _inner.ReadOnly;
        set => _inner.ReadOnly = value;
    }

    public HorizontalAlignment TextAlign
    {
        get => _inner.TextAlign;
        set => _inner.TextAlign = value;
    }

    public void SetToolTip(ToolTip toolTip, string text)
    {
        toolTip.SetToolTip(this, text);
        toolTip.SetToolTip(_inner, text);
    }

    public bool HasError
    {
        get => _hasError;
        set { _hasError = value; Invalidate(); }
    }

    public VisualState? ForcedState
    {
        get => _forcedState;
        set { _forcedState = value; Invalidate(); }
    }

    public AppTextBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                  ControlStyles.Selectable, true);
        Size = new Size(220, Metrics.InputHeight);
        BackColor = Colors.Surface;
        Cursor = Cursors.IBeam;

        _inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = Fonts.Body,
            ForeColor = Colors.TextPrimary,
            BackColor = Colors.Surface,
            Dock = DockStyle.None,
            Cursor = Cursors.IBeam,
            TabStop = true
        };
        _inner.GotFocus += (_, _) => Invalidate();
        _inner.LostFocus += (_, _) => Invalidate();
        _inner.TextChanged += (_, _) => TextChanged2?.Invoke(this, EventArgs.Empty);
        _inner.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            EnterPressed?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(_inner);
        MouseDown += (_, _) => _inner.Focus();
    }

    private VisualState EffectiveState()
    {
        if (_forcedState.HasValue) return _forcedState.Value;
        if (!Enabled) return VisualState.Disabled;
        if (_hasError) return VisualState.Error;
        if (_inner.Focused) return VisualState.Focused;
        return VisualState.Normal;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (_inner == null) return;
        const int horizontalInset = 12;
        _inner.SetBounds(
            horizontalInset,
            Math.Max(1, (Height - _inner.PreferredHeight) / 2),
            Math.Max(1, Width - horizontalInset * 2),
            _inner.PreferredHeight);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _inner.Enabled = Enabled;
        _inner.BackColor = Enabled ? Colors.Surface : Colors.AppBackground;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        GraphicsHelper.SetHighQuality(g);
        var state = EffectiveState();

        Color border = state switch
        {
            VisualState.Error => Colors.Danger,
            VisualState.Focused => Colors.Primary,
            VisualState.Disabled => Colors.Divider,
            _ => Colors.Border
        };
        float width = state is VisualState.Focused or VisualState.Error ? 1.6f : 1f;

        var rect = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = GraphicsHelper.RoundedRect(rect, Metrics.InputRadius);
        using var bg = new SolidBrush(Enabled ? Colors.Surface : Colors.AppBackground);
        g.FillPath(bg, path);
        using var pen = new Pen(border, width);
        g.DrawPath(pen, path);
    }
}
