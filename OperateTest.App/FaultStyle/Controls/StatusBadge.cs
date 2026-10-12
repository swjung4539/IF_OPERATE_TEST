using System.Drawing;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>COMPONENT_INTERACTION_RULES.md 7절 상태 표현. 색만이 아니라 텍스트+점(아이콘)을 함께 표시한다.</summary>
public enum AppStatus
{
    Connected,
    Connecting,
    Failed,
    Ready,
    Running,
    Pass,
    Fail,
    ErrorState,
    Stopped,
    NotRun,
    Unsupported
}

public class StatusBadge : Control
{
    private AppStatus _status = AppStatus.NotRun;

    public AppStatus Status
    {
        get => _status;
        set { _status = value; AutoResize(); Invalidate(); }
    }

    public StatusBadge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = Fonts.Caption;
        Height = 26;
        AutoResize();
    }

    private (string text, Color color) Info() => _status switch
    {
        AppStatus.Connected => ("연결됨", Colors.Success),
        AppStatus.Connecting => ("연결 중", Colors.Info),
        AppStatus.Failed => ("연결 실패", Colors.Danger),
        AppStatus.Ready => ("준비됨", Colors.Info),
        AppStatus.Running => ("실행 중", Colors.Info),
        AppStatus.Pass => ("PASS", Colors.Success),
        AppStatus.Fail => ("FAIL", Colors.Danger),
        AppStatus.ErrorState => ("오류", Colors.Warning),
        AppStatus.Stopped => ("중단", Colors.TextMuted),
        AppStatus.Unsupported => ("미지원", Colors.Neutral),
        _ => ("미실행", Colors.Neutral)
    };

    private void AutoResize()
    {
        var (text, _) = Info();
        int w = TextRenderer.MeasureText(text, Font).Width + 34;
        Width = Math.Max(w, 60);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        GraphicsHelper.SetHighQuality(g);
        var (text, color) = Info();

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = GraphicsHelper.RoundedRect(rect, Height / 2);
        var surface = Color.FromArgb(28, color);
        using (var b = new SolidBrush(surface)) g.FillPath(b, path);
        using (var p = new Pen(color, 1f)) g.DrawPath(p, path);

        var dot = new Rectangle(12, Height / 2 - 4, 8, 8);
        using (var db = new SolidBrush(color)) g.FillEllipse(db, dot);

        var textRect = new Rectangle(dot.Right + 6, 0, Width - dot.Right - 12, Height);
        TextRenderer.DrawText(g, text, Font, textRect, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
