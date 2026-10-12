using OperateTest.App.Models;
using OperateTest.App.Services;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Controls;

public sealed class ManualInspectionControl : Panel
{
    private static readonly (string Key, string Label, string Criterion)[] VisualDefinitions =
    [
        ("missing", "미삽", "부품 미삽이 없을 것"),
        ("reversed", "역삽", "극성 부품 역삽이 없을 것"),
        ("cold_joint", "냉땜", "냉땜 및 접합 불량이 없을 것"),
        ("wrong_part", "오삽", "오삽 및 사양 오류가 없을 것"),
        ("foreign", "이물질", "기판 이물질이 없을 것"),
        ("solder_short", "납쇼트", "납쇼트가 없을 것"),
        ("cleaning", "세척", "세척 상태가 양호할 것")
    ];

    private readonly InspectionSession _session;
    private readonly Dictionary<string, ComboBox> _visualSelectors = [];
    private readonly Dictionary<string, ComboBox> _powerSelectors = [];
    private readonly Dictionary<string, AppTextBox> _measurements = [];
    private readonly Label _status = new();
    private readonly Action? _stateChanged;
    private bool _loading = true;

    public ManualInspectionControl(InspectionSession session, Action? stateChanged = null)
    {
        _session = session;
        _stateChanged = stateChanged;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        AutoScrollMinSize = new Size(1252, 680);
        BackColor = Colors.AppBackground;

        var visual = new AppPanel { Location = new Point(30, 12), Size = new Size(570, 570) };
        visual.Controls.Add(Title("육안검사", 22, 18, 220));
        visual.Controls.Add(Caption("각 항목을 직접 확인하고 PASS 또는 FAIL을 선택하세요.", 22, 48, 500));
        var vy = 86;
        foreach (var definition in VisualDefinitions)
        {
            AddInspectionRow(visual, definition.Key, definition.Label, definition.Criterion, vy,
                _session.Results.VisualItems, _visualSelectors, null);
            vy += 62;
        }
        Controls.Add(visual);

        var power = new AppPanel { Location = new Point(620, 12), Size = new Size(602, 570) };
        power.Controls.Add(Title("전원검사", 22, 18, 220));
        power.Controls.Add(Caption("LED는 직접 판정하고, 전압은 측정값 입력 시 기준 범위로 자동 판정됩니다.", 22, 48, 550));
        var py = 86;
        foreach (var definition in PowerInspectionCriteriaService.Get(session.BoardType))
        {
            AddInspectionRow(power, definition.Key, definition.Label, definition.Criterion, py,
                _session.Results.PowerItems, _powerSelectors, _measurements, definition.Minimum, definition.Maximum);
            py += 72;
        }
        Controls.Add(power);

        var save = new AppButton
        {
            Variant = session.BoardType == InspectionBoardType.Communication
                ? AppButtonVariant.Primary
                : AppButtonVariant.Warning,
            IconGlyph = AppIcon.Check,
            Text = "육안·전원 결과 임시 저장",
            Location = new Point(922, 598), Size = new Size(300, 48)
        };
        save.Click += (_, _) => SaveResults();
        _status.SetBounds(30, 601, 860, 42);
        _status.Font = Fonts.ButtonText;
        _status.ForeColor = session.Results.ManualInspectionCompleted ? Colors.Success : Colors.TextMuted;
        _status.Text = session.Results.ManualInspectionCompleted
            ? "임시 저장된 결과가 있습니다. 최종 판정 확정 시 Excel에 반영됩니다."
            : "모든 항목을 판정해 임시 저장하세요. Excel 반영은 최종 판정에서 진행됩니다.";
        Controls.Add(_status);
        Controls.Add(save);
        _loading = false;
    }

    private void AddInspectionRow(
        Control parent,
        string key,
        string label,
        string criterion,
        int y,
        Dictionary<string, InspectionItemResult> stored,
        Dictionary<string, ComboBox> selectors,
        Dictionary<string, AppTextBox>? measurements,
        double? minimum = null,
        double? maximum = null)
    {
        if (!stored.TryGetValue(key, out var prior))
        {
            prior = new InspectionItemResult();
            stored[key] = prior;
        }

        parent.Controls.Add(new Label
        {
            Location = new Point(22, y), Size = new Size(105, 22), Font = Fonts.ButtonText,
            ForeColor = Colors.TextPrimary, Text = label
        });
        parent.Controls.Add(new Label
        {
            Location = new Point(22, y + 25), Size = new Size(280, 20), Font = Fonts.Caption,
            ForeColor = Colors.TextSecondary, Text = criterion, AutoEllipsis = true
        });

        AppTextBox? measurement = null;
        if (measurements is not null && minimum.HasValue && maximum.HasValue)
        {
            measurement = new AppTextBox
            {
                Location = new Point(310, y + 2), Size = new Size(112, 40),
                TextValue = prior.Measurement
            };
            measurements[key] = measurement;
            parent.Controls.Add(measurement);
            parent.Controls.Add(new Label
            {
                Location = new Point(426, y + 11), Size = new Size(22, 24),
                Font = Fonts.ButtonText, ForeColor = Colors.TextSecondary,
                Text = "V", TextAlign = ContentAlignment.MiddleLeft
            });
        }

        var selectorX = measurements is null ? 410 : 454;
        var selector = new ComboBox
        {
            Location = new Point(selectorX, y + 4), Size = new Size(measurements is null ? 130 : 118, 36),
            Font = Fonts.Body, DropDownStyle = ComboBoxStyle.DropDownList
        };
        selector.Items.AddRange(["선택", "PASS", "FAIL"]);
        selector.SelectedIndex = prior.Passed switch { true => 1, false => 2, _ => 0 };
        selector.SelectedIndexChanged += (_, _) => MarkDirty();
        selectors[key] = selector;
        parent.Controls.Add(selector);

        if (measurement is not null)
        {
            selector.Visible = false;
            var resultIndicator = new Label
            {
                Location = new Point(454, y + 2), Size = new Size(118, 40),
                Font = Fonts.ButtonText, TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle
            };
            parent.Controls.Add(resultIndicator);

            void ShowAutomaticResult(string text, Color foreground, Color background)
            {
                resultIndicator.Text = text;
                resultIndicator.ForeColor = foreground;
                resultIndicator.BackColor = background;
            }

            void EvaluateMeasurement()
            {
                var input = measurement.TextValue.Trim();
                prior.Measurement = input;
                var evaluation = PowerInspectionCriteriaService.Evaluate(input, minimum!.Value, maximum!.Value, out _);
                if (evaluation is VoltageEvaluation.Missing or VoltageEvaluation.Invalid)
                {
                    selector.SelectedIndex = 0;
                    measurement.HasError = evaluation == VoltageEvaluation.Invalid;
                    ShowAutomaticResult(
                        evaluation == VoltageEvaluation.Missing ? "값 입력" : "숫자 확인",
                        evaluation == VoltageEvaluation.Missing ? Colors.TextMuted : Colors.Danger,
                        evaluation == VoltageEvaluation.Missing ? Colors.AppBackground : Colors.DangerSurface);
                    return;
                }
                measurement.HasError = false;
                var passed = evaluation == VoltageEvaluation.Pass;
                selector.SelectedIndex = passed ? 1 : 2;
                ShowAutomaticResult(
                    passed ? "PASS" : "FAIL",
                    passed ? Colors.Success : Colors.Danger,
                    passed ? Colors.SuccessSurface : Colors.DangerSurface);
            }
            measurement.TextChanged2 += (_, _) => EvaluateMeasurement();
            measurement.TextChanged2 += (_, _) => MarkDirty();
            EvaluateMeasurement();
        }
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _session.Results.InvalidateFinalization();
        _session.Results.ManualInspectionCompleted = false;
        _session.Results.ManualStatus = InspectionExecutionStatus.NotRun;
        _status.Text = "입력값이 변경되었습니다. [육안·전원 결과 임시 저장]을 눌러 변경사항을 반영하세요.";
        _status.ForeColor = Colors.Warning;
        _stateChanged?.Invoke();
    }

    private void SaveResults()
    {
        if (_visualSelectors.Values.Any(selector => selector.SelectedIndex == 0)
            || _powerSelectors.Values.Any(selector => selector.SelectedIndex == 0))
        {
            _status.Text = "미판정 항목이 있습니다. 육안·LED는 판정을 선택하고 전압 측정값은 숫자로 입력하세요.";
            _status.ForeColor = Colors.Danger;
            return;
        }

        foreach (var pair in _visualSelectors)
            _session.Results.VisualItems[pair.Key].Passed = pair.Value.SelectedIndex == 1;
        foreach (var pair in _powerSelectors)
            _session.Results.PowerItems[pair.Key].Passed = pair.Value.SelectedIndex == 1;
        foreach (var pair in _measurements)
            _session.Results.PowerItems[pair.Key].Measurement = pair.Value.TextValue.Trim();

        var audit = new InspectionAuditService();
        var attempt = audit.BeginAttempt(_session, "manual", "육안 및 전원 측정 결과 저장");
        _session.Results.InvalidateFinalization();
        _session.Results.ManualInspectionCompleted = true;
        var failed = _session.Results.VisualItems.Values.Count(item => item.Passed == false)
                     + _session.Results.PowerItems.Values.Count(item => item.Passed == false);
        _session.Results.ManualStatus = failed == 0
            ? InspectionExecutionStatus.Pass
            : InspectionExecutionStatus.Fail;
        audit.CompleteAttempt(_session, "manual", attempt, _session.Results.ManualStatus, $"FAIL {failed}개");
        new InspectionCheckpointService().TrySave(_session);
        _status.Text = failed == 0
            ? "육안·전원 결과를 임시 저장했습니다. 전체 항목 PASS · 최종 판정 시 Excel 반영"
            : $"육안·전원 결과를 임시 저장했습니다. FAIL {failed}개 · 최종 판정 시 Excel 반영";
        _status.ForeColor = failed == 0 ? Colors.Success : Colors.Danger;
        _stateChanged?.Invoke();
    }

    private static Label Title(string text, int x, int y, int width) => new()
    {
        Location = new Point(x, y), Size = new Size(width, 28), Font = Fonts.SectionTitle,
        ForeColor = Colors.TextPrimary, Text = text
    };

    private static Label Caption(string text, int x, int y, int width) => new()
    {
        Location = new Point(x, y), Size = new Size(width, 22), Font = Fonts.Caption,
        ForeColor = Colors.TextSecondary, Text = text
    };
}
