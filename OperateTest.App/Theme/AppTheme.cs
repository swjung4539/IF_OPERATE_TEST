using System.Drawing.Drawing2D;

namespace OperateTest.App.Theme;

public static class AppColors
{
    public static readonly Color Background = ColorTranslator.FromHtml("#F5F8FC");
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = ColorTranslator.FromHtml("#D8E4F2");
    public static readonly Color Text = ColorTranslator.FromHtml("#10264D");
    public static readonly Color SubText = ColorTranslator.FromHtml("#64748B");
    public static readonly Color Primary = ColorTranslator.FromHtml("#0B63E5");
    public static readonly Color PrimaryHover = ColorTranslator.FromHtml("#0956C8");
    public static readonly Color Comm = ColorTranslator.FromHtml("#0F77D4");
    public static readonly Color Dio = ColorTranslator.FromHtml("#E87923");
    public static readonly Color Success = ColorTranslator.FromHtml("#159447");
    public static readonly Color SuccessSurface = ColorTranslator.FromHtml("#EAF7EF");
    public static readonly Color Danger = ColorTranslator.FromHtml("#E53935");
    public static readonly Color DangerSurface = ColorTranslator.FromHtml("#FDEEEE");
    public static readonly Color NeutralSurface = ColorTranslator.FromHtml("#EEF3F8");
    public static readonly Color SurfaceBlue = ColorTranslator.FromHtml("#F2F7FF");
    public static readonly Color SurfaceBlueStrong = ColorTranslator.FromHtml("#E7F1FF");
    public static readonly Color Divider = ColorTranslator.FromHtml("#E4EBF4");
    public static readonly Color Warning = ColorTranslator.FromHtml("#F57C00");
    public static readonly Color WarningSurface = ColorTranslator.FromHtml("#FFF3E6");
}

public static class AppMetrics
{
    public const int SideMenuWidth = 224;
    public const int SideMenuRowHeight = 58;
    public const int HeaderHeight = 72;
}

public static class AppFonts
{
    private const string Family = "맑은 고딕";
    public static Font PageTitle => new(Family, 21f, FontStyle.Bold);
    public static Font PageSubtitle => new(Family, 10f, FontStyle.Regular);
    public static Font CardTitle => new(Family, 15f, FontStyle.Bold);
    public static Font SectionTitle => new(Family, 12f, FontStyle.Bold);
    public static Font Body => new(Family, 9.5f, FontStyle.Regular);
    public static Font BodyBold => new(Family, 9.5f, FontStyle.Bold);
    public static Font Small => new(Family, 8.5f, FontStyle.Regular);
    public static Font Badge => new(Family, 8.5f, FontStyle.Bold);
}

public static class Drawing
{
    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
