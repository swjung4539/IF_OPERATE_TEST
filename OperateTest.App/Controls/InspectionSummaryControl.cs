using System.Diagnostics;
using OperateTest.App.Models;
using OperateTest.App.Services;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Controls;

public sealed class InspectionSummaryControl : Panel
{
    private readonly InspectionSession _session;
    private readonly Panel _rows = new();
    private readonly Label _message = new();
    private readonly Label _overall = new();
    private readonly AppButton _finalize;
    private readonly AppButton _openExcel;
    private readonly AppButton _openFolder;
    private readonly AppButton _nextBoard;

    public InspectionSummaryControl(InspectionSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = Colors.AppBackground;

        var card = new AppPanel { Location = new Point(30, 12), Size = new Size(1192, 500) };
        card.Controls.Add(new Label { Location = new Point(24, 20), Size = new Size(420, 30), Font = Fonts.SectionTitle, ForeColor = Colors.TextPrimary, Text = "검사 항목 종합" });
        card.Controls.Add(new Label { Location = new Point(24, 52), Size = new Size(900, 22), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = "중단·오류·미실행 항목은 제품 FAIL로 처리하지 않으며, 완료 전 다시 검사해야 합니다." });
        _rows.SetBounds(24, 90, 1144, 270);
        _rows.BackColor = Colors.Surface;
        card.Controls.Add(_rows);

        _overall.SetBounds(24, 370, 500, 36);
        _overall.Font = new Font(Fonts.SectionTitle, FontStyle.Bold);
        card.Controls.Add(_overall);
        _message.SetBounds(24, 412, 700, 58);
        _message.Font = Fonts.Body;
        _message.ForeColor = Colors.TextSecondary;
        card.Controls.Add(_message);

        var refresh = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.Refresh, Text = "결과 새로고침", Location = new Point(764, 412), Size = new Size(160, 48) };
        refresh.Click += (_, _) => RefreshView();
        card.Controls.Add(refresh);
        _finalize = new AppButton { Variant = session.BoardType == InspectionBoardType.Communication ? AppButtonVariant.Primary : AppButtonVariant.Warning, IconGlyph = AppIcon.Check, Text = "판정 확정 및 Excel 저장", Location = new Point(940, 412), Size = new Size(228, 48) };
        _finalize.Click += (_, _) => FinalizeInspection();
        card.Controls.Add(_finalize);
        Controls.Add(card);

        var info = new AppPanel { Location = new Point(30, 530), Size = new Size(1192, 136) };
        info.Controls.Add(new Label { Location = new Point(24, 12), Size = new Size(220, 25), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, Text = "결과 파일" });
        info.Controls.Add(new Label { Location = new Point(24, 39), Size = new Size(1138, 25), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = session.ResultPath, AutoEllipsis = true });
        _openExcel = new AppButton { Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.FolderOpen, Text = "Excel 파일 열기", Location = new Point(520, 76), Size = new Size(190, 42) };
        _openFolder = new AppButton { Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.FolderOpen, Text = "저장 폴더 열기", Location = new Point(724, 76), Size = new Size(190, 42) };
        _nextBoard = new AppButton { Variant = AppButtonVariant.Primary, IconGlyph = AppIcon.ChevronRight, Text = "다음 보드 검사", Location = new Point(928, 76), Size = new Size(216, 42) };
        _openExcel.Click += (_, _) => OpenPath(_session.ResultPath);
        _openFolder.Click += (_, _) => OpenPath(Path.GetDirectoryName(_session.ResultPath)!);
        _nextBoard.Click += (_, _) => FindForm()?.Close();
        info.Controls.Add(_openExcel); info.Controls.Add(_openFolder); info.Controls.Add(_nextBoard);
        Controls.Add(info);
        RefreshView();
    }

    private IReadOnlyList<(string Name, InspectionExecutionStatus Status)> RequiredItems()
    {
        var r = _session.Results;
        return _session.BoardType == InspectionBoardType.Communication
            ? [("육안 및 전원 검사", r.ManualStatus), ("통신 검사", r.CommunicationStatus), ("로터리 스위치 검사", r.RotaryStatus), ("FAULT 접점 검사", r.FaultStatus)]
            : [("육안 및 전원 검사", r.ManualStatus), ("DO-DI 루프백 검사", r.DioLoopbackStatus), ("ID DIP 스위치 검사", r.DipStatus), ("DO 전체 OFF 안전 확인", SafetyStatus(r.DioSafetyStatus))];
    }

    private void RefreshView()
    {
        _rows.Controls.Clear();
        var items = RequiredItems();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var y = i * 66;
            _rows.Controls.Add(new Label { Location = new Point(18, y + 18), Size = new Size(480, 28), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, Text = item.Name });
            _rows.Controls.Add(new StatusBadge { Location = new Point(940, y + 16), Status = ToAppStatus(item.Status) });
            if (i < items.Count - 1) _rows.Controls.Add(new Panel { Location = new Point(18, y + 65), Size = new Size(1108, 1), BackColor = Colors.Divider });
        }

        if (_session.Results.IsFinalized)
        {
            var pass = _session.Results.OverallPassed == true;
            _overall.Text = pass ? "최종 판정: PASS" : "최종 판정: FAIL";
            _overall.ForeColor = pass ? Colors.Success : Colors.Danger;
            _message.Text = _session.Results.WorkbookSavedAt.HasValue
                ? $"최종 판정과 세부 결과를 Excel에 저장했습니다. ({_session.Results.WorkbookSavedAt:yyyy-MM-dd HH:mm:ss})"
                : $"{_session.Results.FinalizedAt:yyyy-MM-dd HH:mm:ss}에 판정을 확정했습니다.";
        }
        else
        {
            _overall.Text = "최종 판정: 미확정";
            _overall.ForeColor = Colors.TextMuted;
            _message.Text = "모든 필수 항목을 완료한 뒤 최종 판정을 확정하세요.";
        }
        var excelSaved = _session.Results.IsFinalized && _session.Results.WorkbookSavedAt.HasValue;
        _finalize.Enabled = !excelSaved;
        _openExcel.Enabled = excelSaved && File.Exists(_session.ResultPath);
        _openFolder.Enabled = excelSaved && Directory.Exists(Path.GetDirectoryName(_session.ResultPath));
        _nextBoard.Enabled = excelSaved;
    }

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"경로를 열 수 없습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}", "열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void FinalizeInspection()
    {
        var items = RequiredItems();
        var incomplete = items.Where(x => x.Status is not InspectionExecutionStatus.Pass and not InspectionExecutionStatus.Fail).ToArray();
        if (incomplete.Length > 0)
        {
            var list = string.Join(Environment.NewLine, incomplete.Select(x => $"• {x.Name}: {StatusText(x.Status)}"));
            MessageBox.Show(this, $"다음 필수 검사를 완료해야 합니다.{Environment.NewLine}{Environment.NewLine}{list}", "최종 판정 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _message.Text = "미완료·중단·오류 항목이 있어 최종 판정을 확정할 수 없습니다.";
            _message.ForeColor = Colors.Warning;
            return;
        }
        _session.Results.OverallPassed = items.All(x => x.Status == InspectionExecutionStatus.Pass);
        _session.Results.IsFinalized = true;
        _session.Results.FinalizedAt = DateTime.Now;
        try
        {
            new InspectionWorkbookService().SaveFinalResults(_session);
            _message.ForeColor = Colors.Success;
            RefreshView();
            MessageBox.Show(this,
                $"최종 판정과 검사 결과를 Excel 파일에 저장했습니다.{Environment.NewLine}{Environment.NewLine}{_session.ResultPath}",
                "검사 결과 저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _session.Results.OverallPassed = null;
            _session.Results.IsFinalized = false;
            _session.Results.FinalizedAt = null;
            _session.Results.WorkbookSavedAt = null;
            _overall.Text = "최종 판정: 저장 실패";
            _overall.ForeColor = Colors.Danger;
            _message.Text = "Excel 파일을 닫은 뒤 다시 시도하세요.";
            _message.ForeColor = Colors.Danger;
            MessageBox.Show(this,
                $"Excel 결과를 저장하지 못했습니다.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "검사 결과 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static InspectionExecutionStatus SafetyStatus(DioSafetyStatus status) => status switch
    {
        DioSafetyStatus.OffVerified => InspectionExecutionStatus.Pass,
        DioSafetyStatus.Failed => InspectionExecutionStatus.Error,
        DioSafetyStatus.Restoring => InspectionExecutionStatus.Running,
        _ => InspectionExecutionStatus.NotRun
    };

    private static AppStatus ToAppStatus(InspectionExecutionStatus status) => status switch
    {
        InspectionExecutionStatus.Running => AppStatus.Running,
        InspectionExecutionStatus.Pass => AppStatus.Pass,
        InspectionExecutionStatus.Fail => AppStatus.Fail,
        InspectionExecutionStatus.Stopped => AppStatus.Stopped,
        InspectionExecutionStatus.Error => AppStatus.ErrorState,
        _ => AppStatus.NotRun
    };

    private static string StatusText(InspectionExecutionStatus status) => status switch
    {
        InspectionExecutionStatus.Running => "검사 중",
        InspectionExecutionStatus.Pass => "PASS",
        InspectionExecutionStatus.Fail => "FAIL",
        InspectionExecutionStatus.Stopped => "중단",
        InspectionExecutionStatus.Error => "오류",
        _ => "미실행"
    };
}
