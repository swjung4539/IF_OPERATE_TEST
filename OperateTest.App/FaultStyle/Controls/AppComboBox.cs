using System.Drawing;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>
/// 기본 WinForms ComboBox 스타일을 그대로 쓰지 않기 위해 완전 커스텀으로 구현한 드롭다운.
/// 표시부는 owner-draw 패널, 펼침 목록은 테두리 없는 팝업 폼 안의 owner-draw ListBox로 그린다.
/// </summary>
public class AppComboBox : Panel
{
    private readonly List<string> _items = new();
    private int _selectedIndex = -1;
    private bool _isOpen;
    private bool _isHover;
    private VisualState? _forcedState;
    private Form? _popup;

    public IReadOnlyList<string> Items => _items;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set { _selectedIndex = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }

    public string? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    public event EventHandler? SelectedIndexChanged;

    public VisualState? ForcedState
    {
        get => _forcedState;
        set { _forcedState = value; Invalidate(); }
    }

    public AppComboBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(220, Metrics.InputHeight);
        TabStop = true;
    }

    public void SetItems(IEnumerable<string> items, int selectedIndex = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count > 0 ? Math.Min(selectedIndex, _items.Count - 1) : -1;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _isHover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _isHover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        if (Enabled) TogglePopup();
    }

    private VisualState EffectiveState()
    {
        if (_forcedState.HasValue) return _forcedState.Value;
        if (!Enabled) return VisualState.Disabled;
        if (_isOpen) return VisualState.Selected;
        if (_isHover) return VisualState.Hover;
        if (Focused) return VisualState.Focused;
        return VisualState.Normal;
    }

    private void TogglePopup()
    {
        if (_isOpen) { ClosePopup(); return; }
        if (_items.Count == 0) return;

        _isOpen = true;
        Invalidate();

        var listBox = new ListBox
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 32,
            Font = Fonts.Body,
            IntegralHeight = false
        };
        listBox.Items.AddRange(_items.ToArray());
        listBox.DrawItem += (_, de) =>
        {
            de.DrawBackground();
            bool selected = de.Index == _selectedIndex;
            bool hot = (de.State & DrawItemState.Selected) != 0;
            var bg = selected ? Colors.SurfaceBlueStrong : (hot ? Colors.SurfaceBlue : Colors.Surface);
            using var b = new SolidBrush(bg);
            de.Graphics.FillRectangle(b, de.Bounds);
            if (selected)
            {
                using var pb = new SolidBrush(Colors.Primary);
                de.Graphics.FillRectangle(pb, new Rectangle(de.Bounds.X, de.Bounds.Y, 3, de.Bounds.Height));
            }
            TextRenderer.DrawText(de.Graphics, listBox.Items[de.Index].ToString(), Fonts.Body,
                new Rectangle(de.Bounds.X + 14, de.Bounds.Y, de.Bounds.Width - 14, de.Bounds.Height),
                Colors.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        listBox.Click += (_, _) =>
        {
            if (listBox.SelectedIndex >= 0) SelectedIndex = listBox.SelectedIndex;
            ClosePopup();
        };

        int popupHeight = Math.Min(_items.Count, 6) * listBox.ItemHeight + 8;
        var screenLoc = Parent!.PointToScreen(new Point(Left, Bottom + 2));

        _popup = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormPosition(),
            ShowInTaskbar = false,
            TopMost = true,
            Size = new Size(Width, popupHeight),
            Location = screenLoc,
            BackColor = Colors.Border
        };
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Colors.Border };
        listBox.Dock = DockStyle.Fill;
        listBox.BackColor = Colors.Surface;
        host.Controls.Add(listBox);
        _popup.Controls.Add(host);
        _popup.Deactivate += (_, _) => ClosePopup();
        _popup.Show(this);
    }

    private static FormStartPosition FormPosition() => FormStartPosition.Manual;

    private void ClosePopup()
    {
        _isOpen = false;
        _popup?.Close();
        _popup?.Dispose();
        _popup = null;
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
            VisualState.Focused or VisualState.Selected => Colors.Primary,
            VisualState.Disabled => Colors.Divider,
            _ => Colors.Border
        };

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = GraphicsHelper.RoundedRect(rect, Metrics.InputRadius);
        using var bg = new SolidBrush(Enabled ? Colors.Surface : Colors.AppBackground);
        g.FillPath(bg, path);
        using var pen = new Pen(border, state is VisualState.Focused or VisualState.Selected ? 1.6f : 1f);
        g.DrawPath(pen, path);

        var textColor = Enabled ? Colors.TextPrimary : Colors.TextMuted;
        var textRect = new Rectangle(10, 0, Width - 34, Height);
        TextRenderer.DrawText(g, SelectedItem ?? string.Empty, Fonts.Body, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var arrowRect = new Rectangle(Width - 24, Height / 2 - 5, 10, 10);
        IconPainter.Draw(g, AppIcon.ChevronDown, arrowRect, Enabled ? Colors.TextSecondary : Colors.TextMuted, 1.4f);
    }
}
