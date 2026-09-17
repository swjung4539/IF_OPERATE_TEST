using System.Drawing.Drawing2D;

namespace TestMode.App;

// A read-only visual: mouse input never changes a switch value.
internal sealed class SwitchDrawing : Control
{
    public Board Board { get; }
    public int? Value { get; set; }
    public bool? Dip1 { get; set; }
    public bool? Dip2 { get; set; }
    public SwitchDrawing(Board board)
    {
        Board = board;
        DoubleBuffered = true;
        Size = new Size(300, 286);
        BackColor = Color.White;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = board == Board.Main ? "로터리 스위치 상태" : "ID DIP 스위치 상태";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(Width / 300f, Height / 286f);
        using var stroke = new Pen(Ui.Border, 2);
        using var blue = new SolidBrush(Ui.Blue);
        using var muted = new SolidBrush(Ui.Muted);
        using var pale = new SolidBrush(Ui.PaleBlue);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        if (Board == Board.Main)
        {
            g.FillEllipse(pale, 63, 52, 174, 174);
            g.DrawEllipse(stroke, 63, 52, 174, 174);
            for (var i = 0; i < 16; i++)
            {
                var angle = (i * 22.5 - 90) * Math.PI / 180;
                var x = 150 + (float)Math.Cos(angle) * 117;
                var y = 139 + (float)Math.Sin(angle) * 117;
                if (Value == i) g.FillEllipse(blue, x - 15, y - 15, 30, 30);
                g.DrawString(i.ToString("X"), Ui.Bold, Value == i ? Brushes.White : muted, new RectangleF(x - 17, y - 16, 34, 32), center);
            }
            if (Value is int value)
            {
                var angle = (value * 22.5 - 90) * Math.PI / 180;
                using var needle = new Pen(Ui.Blue, 7) { EndCap = LineCap.Round };
                g.DrawLine(needle, 150, 139, 150 + (float)Math.Cos(angle) * 60, 139 + (float)Math.Sin(angle) * 60);
                g.FillEllipse(blue, 138, 127, 24, 24);
            }
            else g.DrawString("—", Ui.Metric, muted, new RectangleF(90, 89, 120, 100), center);
        }
        else
        {
            g.FillRectangle(pale, 38, 32, 224, 218);
            g.DrawRectangle(stroke, 38, 32, 224, 218);
            g.DrawString("ON", Ui.Bold, blue, new RectangleF(44, 38, 210, 30), center);
            for (var i = 0; i < 2; i++)
            {
                var state = i == 0 ? Dip1 : Dip2;
                var x = 80 + i * 88;
                g.FillRectangle(Brushes.White, x, 84, 52, 110);
                g.DrawRectangle(stroke, x, 84, 52, 110);
                if (state.HasValue) g.FillRectangle(blue, x + 6, state.Value ? 90 : 150, 40, 38);
                else g.DrawString("?", Ui.Bold, muted, new RectangleF(x, 100, 52, 76), center);
                g.DrawString((i + 1).ToString(), Ui.Bold, muted, new RectangleF(x, 204, 52, 30), center);
            }
        }
    }
}

internal sealed class SwitchView : UserControl
{
    private readonly SwitchDrawing drawing;
    private readonly Label value;
    private readonly Label bits;
    private readonly Label received;
    public SwitchView(Board board)
    {
        Dock = DockStyle.Fill;
        var card = new Surface { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var title = Ui.Label(board == Board.Main ? "로터리 스위치" : "ID DIP 스위치", Ui.Bold);
        var subtitle = Ui.Label("보드의 스위치를 직접 조작하면 현재 상태가 표시됩니다.", color: Ui.Muted);
        var top = Ui.Stack(title, subtitle);
        drawing = new SwitchDrawing(board);
        value = Ui.Label("—", Ui.Metric);
        value.Name = "switchValue";
        bits = Ui.Label(board == Board.Main ? "입력 비트   — — — —" : "스위치 1   —     스위치 2   —", color: Ui.Muted);
        bits.Name = "switchBits";
        received = Ui.Label("수신 대기", color: Ui.Muted);
        received.Name = "switchReceived";
        var values = Ui.Stack(Ui.Label(board == Board.Main ? "현재 로터리 값" : "현재 ID", Ui.Bold), value, bits, received);
        values.Padding = new Padding(22, 60, 0, 0);
        var middle = Ui.Table(2, 46, 54);
        middle.Controls.Add(drawing, 0, 0);
        middle.Controls.Add(values, 1, 0);
        var note = Ui.Note("스위치 상태를 확인하는 화면입니다. 연결 전에는 값이 표시되지 않습니다.");
        note.Dock = DockStyle.Bottom;
        card.Controls.Add(middle);
        card.Controls.Add(note);
        card.Controls.Add(top);
        Controls.Add(card);
    }

    // Called only by the explicit UI-check harness until real device readback is added.
    internal void SetPreview(int? current, string rawBits, bool? dip1 = null, bool? dip2 = null)
    {
        drawing.Value = current;
        drawing.Dip1 = dip1;
        drawing.Dip2 = dip2;
        drawing.Invalidate();
        value.Text = current is int v ? (drawing.Board == Board.Main ? $"{v:X}  /  {v}" : v.ToString()) : "—";
        bits.Text = rawBits;
        received.Text = current.HasValue ? "예시 데이터 · 실물 수신 아님" : "수신 대기";
    }

    internal void SetDeviceValue(int current)
    {
        SetPreview(current, $"입력 비트   {(current >> 3) & 1} {(current >> 2) & 1} {(current >> 1) & 1} {current & 1}");
        received.Text = "USB 수신";
    }

    internal void SetDisconnected()
    {
        SetPreview(null, "입력 비트   — — — —");
        received.Text = "USB 연결 대기";
    }

    internal void SetDioDeviceValue(bool dip1, bool dip2)
    {
        var id = 1 + (dip1 ? 2 : 0) + (dip2 ? 1 : 0);
        drawing.Value = id;
        drawing.Dip1 = dip1;
        drawing.Dip2 = dip2;
        drawing.Invalidate();
        value.Text = id.ToString();
        bits.Text = $"스위치 1   {(dip1 ? "ON" : "OFF")}     스위치 2   {(dip2 ? "ON" : "OFF")}";
        received.Text = "USB 수신";
    }
}
