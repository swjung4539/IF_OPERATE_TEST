using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TestEmulator.Theme;

/// <summary>DESIGN_TOKENS.md / ai_work/03-ui-ux/DESIGN_SYSTEM.md 2절 색상 토큰 그대로 채택.</summary>
public static class Colors
{
    public static readonly Color AppBackground = ColorTranslator.FromHtml("#F7F9FC");
    public static readonly Color Surface = ColorTranslator.FromHtml("#FFFFFF");
    public static readonly Color SurfaceBlue = ColorTranslator.FromHtml("#F2F7FF");
    public static readonly Color SurfaceBlueStrong = ColorTranslator.FromHtml("#E7F1FF");
    public static readonly Color Border = ColorTranslator.FromHtml("#D8E4F2");
    public static readonly Color BorderStrong = ColorTranslator.FromHtml("#B7CBE5");
    public static readonly Color Divider = ColorTranslator.FromHtml("#E4EBF4");
    public static readonly Color TextPrimary = ColorTranslator.FromHtml("#10264D");
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#526783");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#7C8CA3");
    public static readonly Color Primary = ColorTranslator.FromHtml("#0B63E5");
    public static readonly Color PrimaryHover = ColorTranslator.FromHtml("#0956C8");
    public static readonly Color PrimaryPressed = ColorTranslator.FromHtml("#0849A9");
    public static readonly Color PrimaryDisabled = ColorTranslator.FromHtml("#AFC8ED");
    public static readonly Color Success = ColorTranslator.FromHtml("#159447");
    public static readonly Color SuccessSurface = ColorTranslator.FromHtml("#EAF7EF");
    public static readonly Color Danger = ColorTranslator.FromHtml("#E53935");
    public static readonly Color DangerSurface = ColorTranslator.FromHtml("#FDEEEE");
    public static readonly Color Warning = ColorTranslator.FromHtml("#F57C00");
    public static readonly Color WarningSurface = ColorTranslator.FromHtml("#FFF3E6");
    public static readonly Color Info = ColorTranslator.FromHtml("#1565C0");
    public static readonly Color Neutral = ColorTranslator.FromHtml("#65758B");
}

/// <summary>DESIGN_SYSTEM.md 4절 4px 배수 간격 체계.</summary>
public static class Spacing
{
    public const int S1 = 4;
    public const int S2 = 8;
    public const int S3 = 12;
    public const int S4 = 16;
    public const int S5 = 20;
    public const int S6 = 24;
    public const int S8 = 32;
    public const int S10 = 40;
}

/// <summary>DESIGN_SYSTEM.md 3절 폰트 체계. FONT_SPEC.md가 확인한 대로 OS 내장 맑은 고딕/Consolas에 의존한다.</summary>
public static class Fonts
{
    private const string UiFamily = "맑은 고딕";
    private const string MonoFamily = "Consolas";

    public static Font PageTitle => new(UiFamily, 18f, FontStyle.Bold);
    public static Font CardTitle => new(UiFamily, 14f, FontStyle.Bold);
    public static Font SectionTitle => new(UiFamily, 11f, FontStyle.Bold);
    public static Font MetricValue => new(UiFamily, 15f, FontStyle.Bold);
    public static Font ButtonText => new(UiFamily, 9.5f, FontStyle.Bold);
    public static Font Body => new(UiFamily, 9.5f, FontStyle.Regular);
    public static Font Caption => new(UiFamily, 8.5f, FontStyle.Regular);
    public static Font PacketHex => new(MonoFamily, 9f, FontStyle.Regular);
}

/// <summary>DESIGN_SYSTEM.md 5절 크기·모서리 토큰.</summary>
public static class Metrics
{
    public const int InputHeight = 34;
    public const int ButtonHeight = 40;
    public const int InputRadius = 6;
    public const int ButtonRadius = 7;
    public const int CardRadius = 9;
    public const int BadgeRadius = 999; // 높이 50%
    public const int ToggleWidth = 54;
    public const int ToggleHeight = 26;
    public const int SideMenuWidth = 236;
    public const int SideMenuRowHeight = 64;
    public const int HeaderHeight = 76;
}

public static class GraphicsHelper
{
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        int d = radius * 2;
        path.StartFigure();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void SetHighQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
}

/// <summary>모든 커스텀 컨트롤이 공유하는 시각 상태. AGENTS 2단계 지침의 7개 상태.</summary>
public enum VisualState
{
    Normal,
    Hover,
    Pressed,
    Focused,
    Selected,
    Disabled,
    Error
}
