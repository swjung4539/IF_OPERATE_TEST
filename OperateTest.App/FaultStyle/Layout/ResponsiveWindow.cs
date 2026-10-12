using System.Drawing;
using System.Windows.Forms;

namespace TestEmulator.Layout;

/// <summary>
/// WinForms 창의 논리 설계 크기를 유지하면서 현재 모니터의 작업 영역과 DPI에 맞게
/// 초기 크기를 제한한다. 고정 좌표 화면도 잘리지 않고 스크롤로 접근할 수 있도록 한다.
/// </summary>
public static class ResponsiveWindow
{
    public static void Configure(
        Form form,
        Size preferredClientSize,
        Size minimumWindowSize,
        bool scrollWhenCompact = false)
    {
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = preferredClientSize;
        form.MinimumSize = minimumWindowSize;
        form.AutoScroll = scrollWhenCompact;
        if (scrollWhenCompact)
            form.AutoScrollMinSize = preferredClientSize;

        form.Load += (_, _) => FitToWorkingArea(form, minimumWindowSize);
        form.DpiChanged += (_, _) => form.BeginInvoke(() => FitToWorkingArea(form, minimumWindowSize));
    }

    private static void FitToWorkingArea(Form form, Size requestedMinimum)
    {
        var area = Screen.FromControl(form).WorkingArea;
        var availableWidth = Math.Max(640, area.Width - 16);
        var availableHeight = Math.Max(480, area.Height - 16);

        form.MinimumSize = new Size(
            Math.Min(requestedMinimum.Width, availableWidth),
            Math.Min(requestedMinimum.Height, availableHeight));

        if (form.Width > availableWidth || form.Height > availableHeight)
            form.Size = new Size(Math.Min(form.Width, availableWidth), Math.Min(form.Height, availableHeight));

        if (form.StartPosition == FormStartPosition.Manual)
            return;

        var x = area.Left + Math.Max(0, (area.Width - form.Width) / 2);
        var y = area.Top + Math.Max(0, (area.Height - form.Height) / 2);
        form.Location = new Point(x, y);
    }
}
