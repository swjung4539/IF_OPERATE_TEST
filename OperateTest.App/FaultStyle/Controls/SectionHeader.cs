using System.Drawing;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>DESIGN_SPEC.md "섹션 제목 15~18pt Bold" 패턴. 예: UI-04 "1. 물리 통신 연결".</summary>
public class SectionHeader : Control
{
    private string _subtitle = string.Empty;

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Invalidate(); }
    }

    public SectionHeader()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                  ControlStyles.SupportsTransparentBackColor, true);
        Font = Fonts.SectionTitle;
        Height = string.IsNullOrEmpty(_subtitle) ? 28 : 46;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        GraphicsHelper.SetHighQuality(g);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, 24), Colors.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(_subtitle))
        {
            TextRenderer.DrawText(g, _subtitle, Fonts.Caption, new Rectangle(0, 26, Width, 20), Colors.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
    }
}
