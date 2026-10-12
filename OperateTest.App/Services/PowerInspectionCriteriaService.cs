using System.Globalization;
using OperateTest.App.Models;

namespace OperateTest.App.Services;

public sealed record PowerInspectionCriterion(
    string Key,
    string Label,
    string Criterion,
    double? Minimum,
    double? Maximum);

public enum VoltageEvaluation
{
    Missing,
    Invalid,
    Pass,
    Fail
}

public static class PowerInspectionCriteriaService
{
    private static readonly PowerInspectionCriterion[] Communication =
    [
        new("power_led", "LED1", "녹색 LED 점등", null, null),
        new("input_24v", "C27 양단", "23.5V~24.5V", 23.5, 24.5),
        new("tp1_5v", "TP1–TP3", "4.9V~5.1V", 4.9, 5.1),
        new("tp2_3v3", "TP2–GND_ISO1", "3.2V~3.4V", 3.2, 3.4),
        new("c37_5v", "C37 양단", "4.9V~5.1V", 4.9, 5.1),
        new("c41_5v", "C41 양단", "4.9V~5.1V", 4.9, 5.1)
    ];

    private static readonly PowerInspectionCriterion[] DigitalIo =
    [
        new("power_led", "LED35", "녹색 LED 점등", null, null),
        new("input_24v", "C27 양단", "23.5V~24.5V", 23.5, 24.5),
        new("c30_5v", "C30 양단", "4.9V~5.1V", 4.9, 5.1),
        new("tp3_3v3", "TP3–GND_ISO1", "3.2V~3.4V", 3.2, 3.4)
    ];

    public static IReadOnlyList<PowerInspectionCriterion> Get(InspectionBoardType boardType) =>
        boardType == InspectionBoardType.Communication ? Communication : DigitalIo;

    public static VoltageEvaluation Evaluate(string input, double minimum, double maximum, out double value)
    {
        input = input.Trim();
        if (input.Length == 0)
        {
            value = default;
            return VoltageEvaluation.Missing;
        }

        var normalized = input.Replace("V", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var parsed = double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                     || double.TryParse(normalized.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (!parsed || double.IsNaN(value) || double.IsInfinity(value)) return VoltageEvaluation.Invalid;
        return value >= minimum && value <= maximum ? VoltageEvaluation.Pass : VoltageEvaluation.Fail;
    }
}
