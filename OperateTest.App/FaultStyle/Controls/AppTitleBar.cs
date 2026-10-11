using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TestEmulator.Theme;

namespace TestEmulator.Controls;

/// <summary>
/// UI-01/02 캡처에서 보이는 커스텀 타이틀바(로고+제품명, 최소화/최대화/닫기)를 재현한다.
/// FormBorderStyle.None 부모 폼 상단에 Dock.Top으로 붙여 쓴다.
/// </summary>
public class AppTitleBar : Panel
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    private readonly Label _titleLabel;
    private readonly TitleBarButton _minBtn;
    private readonly TitleBarButton _maxBtn;
    private readonly TitleBarButton _closeBtn;

    public string ProductTitle
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    public AppTitleBar()
    {
        Height = 56;
        Dock = DockStyle.Top;
        BackColor = Colors.Surface;

        var logo = new LogoGlyph { Size = new Size(28, 28), Location = new Point(20, 14) };
        Controls.Add(logo);

        _titleLabel = new Label
        {
            AutoSize = true,
            Font = Fonts.CardTitle,
            ForeColor = Colors.TextPrimary,
            BackColor = Color.Transparent,
            Location = new Point(56, 15),
            Text = "Test Emulator"
        };
        Controls.Add(_titleLabel);

        _closeBtn = new TitleBarButton(AppIcon.Close, isClose: true) { Size = new Size(46, 56) };
        _maxBtn = new TitleBarButton(AppIcon.Plus /*placeholder, drawn as square below*/, isMax: true) { Size = new Size(46, 56) };
        _minBtn = new TitleBarButton(AppIcon.Plus, isMin: true) { Size = new Size(46, 56) };
        Controls.Add(_closeBtn);
        Controls.Add(_maxBtn);
        Controls.Add(_minBtn);

        _closeBtn.Click += (_, _) => FindForm()?.Close();
        _minBtn.Click += (_, _) => { var f = FindForm(); if (f != null) f.WindowState = FormWindowState.Minimized; };
        _maxBtn.Click += (_, _) =>
        {
            var f = FindForm();
            if (f != null)
                f.WindowState = f.WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
        };

        MouseDown += (_, e) => DragMove(e);
        _titleLabel.MouseDown += (_, e) => DragMove(e);
        logo.MouseDown += (_, e) => DragMove(e);

        Resize += (_, _) => LayoutButtons();
    }

    private void DragMove(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var f = FindForm();
        if (f == null) return;
        ReleaseCapture();
        SendMessage(f.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
    }

    private bool _closeConfirmHooked;
    private bool _suppressNextClose;
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        LayoutButtons();
        if (!_closeConfirmHooked && FindForm() is Form f)
        {
            _closeConfirmHooked = true;
            f.FormClosing += ConfirmClose;
        }
    }

    /// <summary>내부 화면 전환(<see cref="AppNavigation.GoTo"/>)이 호출하는 <c>Form.Close()</c>는
    /// 실제 앱 종료가 아니므로 다음 1회의 종료 확인 팝업만 건너뛴다.</summary>
    public void SuppressNextClose() => _suppressNextClose = true;

    /// <summary>7단계 "종료 확인" 요구사항: 사용자가 취소하면 창을 닫지 않는다.
    /// 단, 화면 전환(<see cref="SuppressNextClose"/>) 직후의 Close()에는 묻지 않는다 — 그렇지 않으면
    /// 화면을 옮길 때마다 "앱을 종료하시겠습니까?"가 뜨고, 취소 시 이전 창이 닫히지 않아 화면이
    /// 겹쳐 쌓이는 버그가 있었다.</summary>
    private void ConfirmClose(object? sender, FormClosingEventArgs e)
    {
        if (_suppressNextClose) { _suppressNextClose = false; return; }
        if (e.CloseReason != CloseReason.UserClosing) return;
        var result = MessageBox.Show($"{FindForm()?.Text ?? _titleLabel.Text}를 종료하시겠습니까?", "종료 확인",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (result != DialogResult.OK) e.Cancel = true;
    }

    private void LayoutButtons()
    {
        _closeBtn.Location = new Point(Width - 46, 0);
        _maxBtn.Location = new Point(Width - 92, 0);
        _minBtn.Location = new Point(Width - 138, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Colors.Divider, 1f);
        e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
    }

    private class LogoGlyph : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            GraphicsHelper.SetHighQuality(g);
            using var path = GraphicsHelper.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 7);
            using var b = new SolidBrush(Colors.Primary);
            g.FillPath(b, path);
            var monitorRect = new Rectangle(6, 7, Width - 12, Height - 16);
            IconPainter.Draw(g, AppIcon.Document, monitorRect, Color.White, 1.6f);
        }
    }

    private class TitleBarButton : Control
    {
        private readonly AppIcon _icon;
        private readonly bool _isClose;
        private readonly bool _isMax;
        private readonly bool _isMin;
        private bool _hover;

        public TitleBarButton(AppIcon icon, bool isClose = false, bool isMax = false, bool isMin = false)
        {
            _icon = icon; _isClose = isClose; _isMax = isMax; _isMin = isMin;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { if (Enabled) { _hover = true; Invalidate(); } base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            GraphicsHelper.SetHighQuality(g);
            if (_hover && Enabled)
            {
                using var b = new SolidBrush(_isClose ? Colors.Danger : Colors.SurfaceBlueStrong);
                g.FillRectangle(b, ClientRectangle);
            }
            var color = !Enabled ? Colors.Divider : _hover && _isClose ? Color.White : Colors.TextSecondary;
            var c = new Point(Width / 2, Height / 2);
            using var pen = new Pen(color, 1.4f);

            if (_isClose)
            {
                g.DrawLine(pen, c.X - 5, c.Y - 5, c.X + 5, c.Y + 5);
                g.DrawLine(pen, c.X + 5, c.Y - 5, c.X - 5, c.Y + 5);
            }
            else if (_isMax)
            {
                g.DrawRectangle(pen, c.X - 5, c.Y - 5, 10, 10);
            }
            else if (_isMin)
            {
                g.DrawLine(pen, c.X - 5, c.Y, c.X + 5, c.Y);
            }
        }
    }
}
