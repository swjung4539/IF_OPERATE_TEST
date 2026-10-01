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
        _maxBtn = new TitleBarButton(AppIcon.Plus /*placeholder, drawn as square below*/, isMax: true) { Size = new Size(46, 56), Enabled = false };
        _minBtn = new TitleBarButton(AppIcon.Plus, isMin: true) { Size = new Size(46, 56) };
        Controls.Add(_closeBtn);
        Controls.Add(_maxBtn);
        Controls.Add(_minBtn);

        _closeBtn.Click += (_, _) => FindForm()?.Close();
        // 이 프로그램은 화면마다 고정 픽셀 좌표로 레이아웃을 짜는 설계라(Anchor/Dock 재배치 없음)
        // 창 크기를 늘리면 레이아웃이 깨진다 — 최대화를 의도적으로 비활성화해 고정 크기를 유지한다.
        _minBtn.Click += (_, _) => { var f = FindForm(); if (f != null) f.WindowState = FormWindowState.Minimized; };

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
    private Size _fixedClientSize;
    private bool _fixedSizeCaptured;
    private bool _enforcingSize;

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        LayoutButtons();
        if (!_closeConfirmHooked && FindForm() is Form f)
        {
            _closeConfirmHooked = true;
            f.FormClosing += ConfirmClose;
            f.Resize += EnforceFixedSize;
            // 이 시점(생성자 초반, AppTitleBar가 막 추가된 직후)의 ClientSize를 그대로 기준으로 삼으면,
            // 생성자 마지막에 ScreenFit(작은 화면에서 전체를 축소하는 로직)이 한 번 더 크기를 바꿀 때
            // EnforceFixedSize가 그 축소를 "고정 크기 위반"으로 보고 즉시 원래(축소 전) 크기로 되돌려버린다.
            // ScreenFit도 Load 시점에 적용되는데, 등록 순서에 따라 이 핸들러가 먼저 실행되면 여전히
            // 축소 전 크기를 기준으로 잡을 위험이 있다 — Load보다 항상 나중에 발생하는 Shown 시점의
            // 최종 크기를 기준으로 삼아 순서 문제를 아예 없앤다.
            f.Shown += (_, _) => { _fixedClientSize = f.ClientSize; _fixedSizeCaptured = true; };
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

    /// <summary>이 프로그램의 모든 화면은 고정 픽셀 좌표 레이아웃이라(Anchor/Dock 재배치 없음) 창
    /// 크기가 설계값과 달라지면 컨트롤이 잘리거나 빈 공간이 생긴다. 최대화 버튼은 비활성화했지만
    /// Windows Aero Snap(제목표시줄을 화면 가장자리로 드래그)이나 더블클릭으로도 상태가 바뀔 수 있어
    /// 여기서 한 번 더 원래 크기로 되돌린다.</summary>
    private void EnforceFixedSize(object? sender, EventArgs e)
    {
        if (_enforcingSize || !_fixedSizeCaptured || sender is not Form f) return;
        // 최소화도 WindowState != Normal이라 이 가드에 걸려서, 최소화 버튼을 누르는 순간 Resize
        // 이벤트가 뜨자마자 여기서 즉시 Normal로 되돌려 "최소화가 가끔 안 먹는다"는 원인이 됐다 —
        // 이 가드는 최대화/Aero Snap만 막으면 되므로 최소화 상태는 그대로 둔다.
        if (f.WindowState == FormWindowState.Minimized) return;
        _enforcingSize = true;
        try
        {
            if (f.WindowState != FormWindowState.Normal) f.WindowState = FormWindowState.Normal;
            if (f.ClientSize != _fixedClientSize) f.ClientSize = _fixedClientSize;
        }
        finally { _enforcingSize = false; }
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
