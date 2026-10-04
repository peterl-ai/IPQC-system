using System.Globalization;
using JaxPower.Ipqc.Api.Domain;

namespace JaxPower.Ipqc.Api.Services;

public sealed class InspectionJudgmentService
{
    public const int MaxSamples = 50;

    public static string? CanonicalType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "qualitative" or "visual" or "定性" => "Qualitative",
        "quantitative" or "numeric" or "计量" or "定量" => "Quantitative",
        _ => null
    };

    public static int RequiredSamples(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0 ? count : 1;

    public Dictionary<string, string[]> Calculate(PatrolTask task, bool strict)
    {
        var errors = new Dictionary<string, string[]>();
        if (strict && task.Shift is not ("Day" or "Night")) errors["Shift"] = ["Select Day or Night shift."];
        if (strict && task.Items.Count == 0) errors["Items"] = ["The Task has no inspection item snapshot."];
        foreach (var item in task.Items.OrderBy(x => x.SequenceNo))
        {
            var field = $"Items[{item.SequenceNo}]";
            if (item.IsNa is null)
            {
                item.JudgmentResult = null;
                if (strict) errors[field] = ["Answer N/A for every item."];
                continue;
            }
            if (item.IsNa == true)
            {
                item.JudgmentResult = "NA";
                continue;
            }
            var type = CanonicalType(item.InspectionType);
            if (type is null)
            {
                item.JudgmentResult = null;
                if (strict) errors[field] = ["Inspection type must be Qualitative or Quantitative in the frozen Standard."];
                continue;
            }
            var required = RequiredSamples(item.SampleCount);
            if (required > MaxSamples)
            {
                item.JudgmentResult = null;
                if (strict) errors[field] = [$"The frozen sample count exceeds the {MaxSamples} sample limit."];
                continue;
            }
            if (type == "Quantitative" && !TryLimits(item, out _, out _, out var limitError))
            {
                item.JudgmentResult = null;
                if (strict) errors[field] = [limitError!];
                continue;
            }
            var complete = 0;
            var anyNg = false;
            foreach (var sample in item.Samples.OrderBy(x => x.SequenceNo))
            {
                if (type == "Qualitative")
                {
                    if (sample.JudgmentResult is not ("OK" or "NG"))
                    {
                        sample.JudgmentResult = null;
                        continue;
                    }
                }
                else
                {
                    sample.JudgmentResult = sample.InspectionValue is { } value &&
                        TryLimits(item, out var lower, out var upper, out _) ?
                        Matches(value, lower) && Matches(value, upper) ? "OK" : "NG" : null;
                }
                if (sample.JudgmentResult is null) continue;
                complete++;
                if (sample.JudgmentResult == "NG") anyNg = true;
            }
            item.JudgmentResult = complete >= required && complete == item.Samples.Count ? (anyNg ? "NG" : "OK") : null;
            if (strict && item.JudgmentResult is null)
                errors[field] = [$"Complete at least {required} valid sample(s), with no incomplete samples."];
        }
        return errors;
    }

    public static string Overall(PatrolTask task) => task.Items.Any(x => x.JudgmentResult == "NG") ? "Unqualified" : "Qualified";

    private static bool TryLimits(PatrolTaskItem item, out (string Op, decimal Value)? lower,
        out (string Op, decimal Value)? upper, out string? error)
    {
        lower = null;
        upper = null;
        error = null;
        if (!string.IsNullOrWhiteSpace(item.LowerLimitOperator) || !string.IsNullOrWhiteSpace(item.LowerLimitValue))
        {
            if (item.LowerLimitOperator is not (">" or ">=") ||
                !decimal.TryParse(item.LowerLimitValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                error = "The frozen lower limit requires > or >= and a numeric value.";
            else lower = (item.LowerLimitOperator, value);
        }
        if (!string.IsNullOrWhiteSpace(item.UpperLimitOperator) || !string.IsNullOrWhiteSpace(item.UpperLimitValue))
        {
            if (item.UpperLimitOperator is not ("<" or "<=") ||
                !decimal.TryParse(item.UpperLimitValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                error = "The frozen upper limit requires < or <= and a numeric value.";
            else upper = (item.UpperLimitOperator, value);
        }
        if (lower is null && upper is null && error is null) error = "The frozen Quantitative item has no usable numeric limits.";
        if (lower is { } lo && upper is { } hi && lo.Value > hi.Value)
            error = "The frozen lower limit exceeds the upper limit.";
        return error is null;
    }

    private static bool Matches(decimal value, (string Op, decimal Value)? bound) => bound is not { } b || b.Op switch
    {
        ">" => value > b.Value,
        ">=" => value >= b.Value,
        "<" => value < b.Value,
        "<=" => value <= b.Value,
        _ => false
    };
}
