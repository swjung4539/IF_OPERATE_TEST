using System.Drawing;
using System.Drawing.Drawing2D;

namespace TestEmulator.Theme;

/// <summary>
/// ICON_RESOURCE_MAP.md 기준 아이콘을 유니코드/이모지 없이 GDI+ 벡터 드로잉으로 렌더링한다.
/// 아이콘 세트 확정 전까지 사용하는 자리표시 벡터 아이콘이며, 실제 아이콘 라이브러리 도입 시 교체한다.
/// </summary>
public enum AppIcon
{
    Home,
    Settings,
    Network,
    Document,
    ChevronRight,
    ChevronLeft,
    ChevronDown,
    Check,
    Close,
    Refresh,
    Send,
    Plus,
    Trash,
    Search,
    Warning,
    Info,
    Save,
    ArrowUp,
    ArrowDown,
    Download,
    FolderOpen,
    Swap,
    Pulse,
    Connector,
    Clipboard,
    Cpu,
    Monitor,
    Server,
    Keyboard,
    Thermometer,
    Flame,
    Handshake,
    Share,
    Shield,
    People,
    Clock,
    Play,
    Bell,
    User
}

public static class IconPainter
{
    public static void Draw(Graphics g, AppIcon icon, Rectangle bounds, Color color, float strokeWidth = 1.6f)
    {
        GraphicsHelper.SetHighQuality(g);
        using var pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        var r = bounds;

        switch (icon)
        {
            case AppIcon.Home:
                g.DrawLine(pen, r.Left, r.Top + r.Height * 0.5f, r.Left + r.Width * 0.5f, r.Top);
                g.DrawLine(pen, r.Left + r.Width * 0.5f, r.Top, r.Right, r.Top + r.Height * 0.5f);
                g.DrawRectangle(pen, r.Left + r.Width * 0.2f, r.Top + r.Height * 0.45f, r.Width * 0.6f, r.Height * 0.45f);
                break;
            case AppIcon.Settings:
                var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                float rad = Math.Min(r.Width, r.Height) / 2f - 1;
                g.DrawEllipse(pen, c.X - rad * 0.45f, c.Y - rad * 0.45f, rad * 0.9f, rad * 0.9f);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    float x1 = c.X + (float)Math.Cos(a) * rad * 0.6f;
                    float y1 = c.Y + (float)Math.Sin(a) * rad * 0.6f;
                    float x2 = c.X + (float)Math.Cos(a) * rad;
                    float y2 = c.Y + (float)Math.Sin(a) * rad;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
                break;
            case AppIcon.Network:
                g.DrawEllipse(pen, r.Left + r.Width * 0.4f, r.Top, r.Width * 0.2f, r.Width * 0.2f);
                g.DrawEllipse(pen, r.Left, r.Bottom - r.Width * 0.2f, r.Width * 0.2f, r.Width * 0.2f);
                g.DrawEllipse(pen, r.Right - r.Width * 0.2f, r.Bottom - r.Width * 0.2f, r.Width * 0.2f, r.Width * 0.2f);
                g.DrawLine(pen, r.Left + r.Width * 0.5f, r.Top + r.Width * 0.2f, r.Left + r.Width * 0.1f, r.Bottom - r.Width * 0.1f);
                g.DrawLine(pen, r.Left + r.Width * 0.5f, r.Top + r.Width * 0.2f, r.Right - r.Width * 0.1f, r.Bottom - r.Width * 0.1f);
                break;
            case AppIcon.Document:
                var docRect = new RectangleF(r.Left + r.Width * 0.15f, r.Top, r.Width * 0.7f, r.Height);
                g.DrawRectangle(pen, docRect.X, docRect.Y, docRect.Width, docRect.Height);
                for (int i = 0; i < 3; i++)
                {
                    float y = docRect.Top + docRect.Height * (0.28f + i * 0.24f);
                    g.DrawLine(pen, docRect.Left + docRect.Width * 0.18f, y, docRect.Right - docRect.Width * 0.18f, y);
                }
                break;
            case AppIcon.ChevronRight:
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.3f, r.Top),
                    new PointF(r.Right - r.Width * 0.3f, r.Top + r.Height * 0.5f),
                    new PointF(r.Left + r.Width * 0.3f, r.Bottom)
                });
                break;
            case AppIcon.ChevronLeft:
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Right - r.Width * 0.3f, r.Top),
                    new PointF(r.Left + r.Width * 0.3f, r.Top + r.Height * 0.5f),
                    new PointF(r.Right - r.Width * 0.3f, r.Bottom)
                });
                break;
            case AppIcon.ChevronDown:
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left, r.Top + r.Height * 0.3f),
                    new PointF(r.Left + r.Width * 0.5f, r.Bottom - r.Height * 0.3f),
                    new PointF(r.Right, r.Top + r.Height * 0.3f)
                });
                break;
            case AppIcon.Check:
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.4f, r.Bottom),
                    new PointF(r.Right, r.Top)
                });
                break;
            case AppIcon.Close:
                g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom);
                break;
            case AppIcon.Refresh:
                g.DrawArc(pen, r.Left, r.Top, r.Width, r.Height, -30, 270);
                var tip = new PointF(r.Right - r.Width * 0.05f, r.Top + r.Height * 0.15f);
                g.FillPolygon(brush, new[]
                {
                    tip,
                    new PointF(tip.X - r.Width * 0.28f, tip.Y - r.Height * 0.05f),
                    new PointF(tip.X - r.Width * 0.08f, tip.Y + r.Height * 0.28f)
                });
                break;
            case AppIcon.Send:
                g.FillPolygon(brush, new[]
                {
                    new PointF(r.Left, r.Top + r.Height * 0.15f),
                    new PointF(r.Right, r.Top + r.Height * 0.5f),
                    new PointF(r.Left, r.Bottom - r.Height * 0.15f),
                    new PointF(r.Left + r.Width * 0.3f, r.Top + r.Height * 0.5f)
                });
                break;
            case AppIcon.Plus:
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Top, r.Left + r.Width / 2f, r.Bottom);
                g.DrawLine(pen, r.Left, r.Top + r.Height / 2f, r.Right, r.Top + r.Height / 2f);
                break;
            case AppIcon.Trash:
                g.DrawRectangle(pen, r.Left + r.Width * 0.2f, r.Top + r.Height * 0.25f, r.Width * 0.6f, r.Height * 0.65f);
                g.DrawLine(pen, r.Left + r.Width * 0.1f, r.Top + r.Height * 0.25f, r.Right - r.Width * 0.1f, r.Top + r.Height * 0.25f);
                g.DrawLine(pen, r.Left + r.Width * 0.38f, r.Top + r.Height * 0.1f, r.Right - r.Width * 0.38f, r.Top + r.Height * 0.1f);
                break;
            case AppIcon.Search:
                float d = r.Width * 0.65f;
                g.DrawEllipse(pen, r.Left, r.Top, d, d);
                g.DrawLine(pen, r.Left + d * 0.85f, r.Top + d * 0.85f, r.Right, r.Bottom);
                break;
            case AppIcon.Warning:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(r.Left + r.Width / 2f, r.Top),
                    new PointF(r.Right, r.Bottom),
                    new PointF(r.Left, r.Bottom)
                });
                g.FillRectangle(brush, r.Left + r.Width * 0.46f, r.Top + r.Height * 0.35f, r.Width * 0.08f, r.Height * 0.25f);
                g.FillEllipse(brush, r.Left + r.Width * 0.45f, r.Bottom - r.Height * 0.2f, r.Width * 0.1f, r.Width * 0.1f);
                break;
            case AppIcon.Info:
                g.DrawEllipse(pen, r.Left, r.Top, r.Width, r.Height);
                g.FillEllipse(brush, r.Left + r.Width * 0.42f, r.Top + r.Height * 0.22f, r.Width * 0.16f, r.Width * 0.16f);
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Top + r.Height * 0.45f, r.Left + r.Width / 2f, r.Bottom - r.Height * 0.2f);
                break;
            case AppIcon.Save:
                g.DrawRectangle(pen, r.Left, r.Top, r.Width, r.Height);
                g.DrawRectangle(pen, r.Left + r.Width * 0.25f, r.Top, r.Width * 0.5f, r.Height * 0.35f);
                break;
            case AppIcon.ArrowUp:
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Bottom, r.Left + r.Width / 2f, r.Top);
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.35f),
                    new PointF(r.Left + r.Width * 0.5f, r.Top),
                    new PointF(r.Right - r.Width * 0.2f, r.Top + r.Height * 0.35f)
                });
                break;
            case AppIcon.ArrowDown:
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Top, r.Left + r.Width / 2f, r.Bottom);
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Bottom - r.Height * 0.35f),
                    new PointF(r.Left + r.Width * 0.5f, r.Bottom),
                    new PointF(r.Right - r.Width * 0.2f, r.Bottom - r.Height * 0.35f)
                });
                break;
            case AppIcon.Download:
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Top, r.Left + r.Width / 2f, r.Top + r.Height * 0.6f);
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.35f),
                    new PointF(r.Left + r.Width * 0.5f, r.Top + r.Height * 0.65f),
                    new PointF(r.Right - r.Width * 0.2f, r.Top + r.Height * 0.35f)
                });
                g.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
                break;
            case AppIcon.FolderOpen:
                g.DrawLine(pen, r.Left, r.Top + r.Height * 0.25f, r.Left + r.Width * 0.4f, r.Top + r.Height * 0.25f);
                g.DrawLine(pen, r.Left + r.Width * 0.4f, r.Top + r.Height * 0.25f, r.Left + r.Width * 0.5f, r.Top + r.Height * 0.4f);
                g.DrawLine(pen, r.Left + r.Width * 0.5f, r.Top + r.Height * 0.4f, r.Right, r.Top + r.Height * 0.4f);
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Right, r.Top + r.Height * 0.4f),
                    new PointF(r.Right - r.Width * 0.1f, r.Bottom),
                    new PointF(r.Left + r.Width * 0.1f, r.Bottom),
                    new PointF(r.Left, r.Top + r.Height * 0.25f)
                });
                break;
            case AppIcon.Swap:
                g.DrawLine(pen, r.Left, r.Top + r.Height * 0.3f, r.Right - r.Width * 0.15f, r.Top + r.Height * 0.3f);
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Right - r.Width * 0.35f, r.Top),
                    new PointF(r.Right, r.Top + r.Height * 0.3f),
                    new PointF(r.Right - r.Width * 0.35f, r.Top + r.Height * 0.6f)
                });
                g.DrawLine(pen, r.Right, r.Bottom - r.Height * 0.3f, r.Left + r.Width * 0.15f, r.Bottom - r.Height * 0.3f);
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.35f, r.Bottom - r.Height * 0.6f),
                    new PointF(r.Left, r.Bottom - r.Height * 0.3f),
                    new PointF(r.Left + r.Width * 0.35f, r.Bottom)
                });
                break;
            case AppIcon.Pulse:
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left, r.Top + r.Height * 0.5f),
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.5f),
                    new PointF(r.Left + r.Width * 0.35f, r.Top),
                    new PointF(r.Left + r.Width * 0.5f, r.Bottom),
                    new PointF(r.Left + r.Width * 0.65f, r.Top + r.Height * 0.5f),
                    new PointF(r.Right, r.Top + r.Height * 0.5f)
                });
                break;
            case AppIcon.Connector:
                g.DrawRectangle(pen, r.Left, r.Top + r.Height * 0.15f, r.Width, r.Height * 0.5f);
                for (int i = 0; i < 3; i++)
                {
                    float px = r.Left + r.Width * (0.2f + i * 0.3f);
                    g.DrawLine(pen, px, r.Top + r.Height * 0.65f, px, r.Bottom);
                }
                break;
            case AppIcon.Clipboard:
                g.DrawRectangle(pen, r.Left + r.Width * 0.15f, r.Top + r.Height * 0.1f, r.Width * 0.7f, r.Height * 0.85f);
                g.DrawRectangle(pen, r.Left + r.Width * 0.35f, r.Top, r.Width * 0.3f, r.Height * 0.15f);
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.3f, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.45f, r.Top + r.Height * 0.7f),
                    new PointF(r.Right - r.Width * 0.28f, r.Top + r.Height * 0.35f)
                });
                break;
            case AppIcon.Cpu:
                g.DrawRectangle(pen, r.Left + r.Width * 0.22f, r.Top + r.Height * 0.22f, r.Width * 0.56f, r.Height * 0.56f);
                for (int i = 0; i < 3; i++)
                {
                    float p = r.Left + r.Width * (0.32f + i * 0.18f);
                    g.DrawLine(pen, p, r.Top, p, r.Top + r.Height * 0.22f);
                    g.DrawLine(pen, p, r.Bottom - r.Height * 0.22f, p, r.Bottom);
                    float pv = r.Top + r.Height * (0.32f + i * 0.18f);
                    g.DrawLine(pen, r.Left, pv, r.Left + r.Width * 0.22f, pv);
                    g.DrawLine(pen, r.Right - r.Width * 0.22f, pv, r.Right, pv);
                }
                break;
            case AppIcon.Monitor:
                g.DrawRectangle(pen, r.Left, r.Top, r.Width, r.Height * 0.7f);
                g.DrawLine(pen, r.Left + r.Width * 0.35f, r.Bottom, r.Left + r.Width * 0.65f, r.Bottom);
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Top + r.Height * 0.7f, r.Left + r.Width / 2f, r.Bottom);
                break;
            case AppIcon.Server:
                g.DrawRectangle(pen, r.Left, r.Top, r.Width, r.Height * 0.42f);
                g.DrawRectangle(pen, r.Left, r.Bottom - r.Height * 0.42f, r.Width, r.Height * 0.42f);
                g.FillEllipse(brush, r.Left + r.Width * 0.12f, r.Top + r.Height * 0.16f, r.Width * 0.1f, r.Width * 0.1f);
                g.FillEllipse(brush, r.Left + r.Width * 0.12f, r.Bottom - r.Height * 0.26f, r.Width * 0.1f, r.Width * 0.1f);
                break;
            case AppIcon.Keyboard:
                g.DrawRectangle(pen, r.Left, r.Top + r.Height * 0.15f, r.Width, r.Height * 0.7f);
                for (int row = 0; row < 2; row++)
                {
                    for (int col = 0; col < 4; col++)
                    {
                        float kx = r.Left + r.Width * (0.12f + col * 0.22f);
                        float ky = r.Top + r.Height * (0.32f + row * 0.28f);
                        g.DrawLine(pen, kx, ky, kx + r.Width * 0.1f, ky);
                    }
                }
                break;
            case AppIcon.Thermometer:
                float tx = r.Left + r.Width * 0.5f;
                g.DrawLine(pen, tx, r.Top, tx, r.Bottom - r.Height * 0.25f);
                g.DrawEllipse(pen, tx - r.Width * 0.18f, r.Bottom - r.Height * 0.36f, r.Width * 0.36f, r.Width * 0.36f);
                g.FillEllipse(brush, tx - r.Width * 0.1f, r.Bottom - r.Height * 0.28f, r.Width * 0.2f, r.Width * 0.2f);
                break;
            case AppIcon.Flame:
                g.DrawClosedCurve(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.5f, r.Top),
                    new PointF(r.Right, r.Top + r.Height * 0.45f),
                    new PointF(r.Left + r.Width * 0.6f, r.Bottom),
                    new PointF(r.Left + r.Width * 0.4f, r.Bottom),
                    new PointF(r.Left, r.Top + r.Height * 0.45f)
                });
                break;
            case AppIcon.Handshake:
                g.DrawLine(pen, r.Left, r.Top + r.Height * 0.4f, r.Left + r.Width * 0.35f, r.Top + r.Height * 0.55f);
                g.DrawLine(pen, r.Left + r.Width * 0.35f, r.Top + r.Height * 0.55f, r.Left + r.Width * 0.5f, r.Top + r.Height * 0.4f);
                g.DrawLine(pen, r.Left + r.Width * 0.5f, r.Top + r.Height * 0.4f, r.Left + r.Width * 0.65f, r.Top + r.Height * 0.55f);
                g.DrawLine(pen, r.Left + r.Width * 0.65f, r.Top + r.Height * 0.55f, r.Right, r.Top + r.Height * 0.4f);
                g.DrawLine(pen, r.Left, r.Top + r.Height * 0.4f, r.Left, r.Top + r.Height * 0.65f);
                g.DrawLine(pen, r.Right, r.Top + r.Height * 0.4f, r.Right, r.Top + r.Height * 0.65f);
                break;
            case AppIcon.Share:
                g.DrawEllipse(pen, r.Left, r.Top + r.Height * 0.4f, r.Width * 0.2f, r.Width * 0.2f);
                g.DrawEllipse(pen, r.Right - r.Width * 0.2f, r.Top, r.Width * 0.2f, r.Width * 0.2f);
                g.DrawEllipse(pen, r.Right - r.Width * 0.2f, r.Bottom - r.Width * 0.2f, r.Width * 0.2f, r.Width * 0.2f);
                g.DrawLine(pen, r.Left + r.Width * 0.18f, r.Top + r.Height * 0.48f, r.Right - r.Width * 0.18f, r.Top + r.Height * 0.12f);
                g.DrawLine(pen, r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f, r.Right - r.Width * 0.18f, r.Bottom - r.Height * 0.12f);
                break;
            case AppIcon.Shield:
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.5f, r.Top),
                    new PointF(r.Right, r.Top + r.Height * 0.2f),
                    new PointF(r.Right, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.5f, r.Bottom),
                    new PointF(r.Left, r.Top + r.Height * 0.55f),
                    new PointF(r.Left, r.Top + r.Height * 0.2f),
                    new PointF(r.Left + r.Width * 0.5f, r.Top)
                });
                break;
            case AppIcon.People:
                g.DrawEllipse(pen, r.Left + r.Width * 0.05f, r.Top, r.Width * 0.36f, r.Width * 0.36f);
                g.DrawEllipse(pen, r.Right - r.Width * 0.41f, r.Top, r.Width * 0.36f, r.Width * 0.36f);
                g.DrawArc(pen, r.Left, r.Top + r.Height * 0.42f, r.Width * 0.5f, r.Height * 0.55f, 180, 180);
                g.DrawArc(pen, r.Right - r.Width * 0.5f, r.Top + r.Height * 0.42f, r.Width * 0.5f, r.Height * 0.55f, 180, 180);
                break;
            case AppIcon.Clock:
                g.DrawEllipse(pen, r.Left, r.Top, r.Width, r.Height);
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Top + r.Height * 0.28f, r.Left + r.Width / 2f, r.Top + r.Height * 0.5f);
                g.DrawLine(pen, r.Left + r.Width / 2f, r.Top + r.Height * 0.5f, r.Left + r.Width * 0.68f, r.Top + r.Height * 0.62f);
                break;
            case AppIcon.Play:
                g.FillPolygon(brush, new[]
                {
                    new PointF(r.Left + r.Width * 0.22f, r.Top),
                    new PointF(r.Right, r.Top + r.Height * 0.5f),
                    new PointF(r.Left + r.Width * 0.22f, r.Bottom)
                });
                break;
            case AppIcon.Bell:
                g.DrawArc(pen, r.Left + r.Width * 0.1f, r.Top, r.Width * 0.8f, r.Height * 0.75f, 180, 180);
                g.DrawLine(pen, r.Left + r.Width * 0.1f, r.Top + r.Height * 0.38f, r.Left, r.Top + r.Height * 0.7f);
                g.DrawLine(pen, r.Right - r.Width * 0.1f, r.Top + r.Height * 0.38f, r.Right, r.Top + r.Height * 0.7f);
                g.DrawLine(pen, r.Left, r.Top + r.Height * 0.7f, r.Right, r.Top + r.Height * 0.7f);
                g.DrawArc(pen, r.Left + r.Width * 0.38f, r.Top + r.Height * 0.72f, r.Width * 0.24f, r.Height * 0.2f, 0, 180);
                break;
            case AppIcon.User:
                g.DrawEllipse(pen, r.Left + r.Width * 0.28f, r.Top, r.Width * 0.44f, r.Width * 0.44f);
                g.DrawArc(pen, r.Left, r.Bottom - r.Height * 0.5f, r.Width, r.Height * 0.55f, 180, 180);
                break;
        }
    }
}
