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

    public static bool TryRequiredSamples(string? value, out int required, out string? error)
    {
        required = 1;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) &&
            count is >= 1 and <= MaxSamples)
        {
            required = count;
            return true;
        }
        error = $"Sample Count must be a whole number from 1 to {MaxSamples}.";
        return false;
    }

    public static string? ValidateDefinition(string? type, string? sampleCount,
        string? lowerOperator, string? lowerValue, string? upperOperator, string? upperValue)
    {
        if (CanonicalType(type) is null) return "Inspection type must be Qualitative or Quantitative.";
        if (!TryRequiredSamples(sampleCount, out _, out var sampleError)) return sampleError;
        return CanonicalType(type) == "Quantitative" &&
            !TryLimits(lowerOperator, lowerValue, upperOperator, upperValue, out _, out _, out var error)
            ? error : null;
    }

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
            if (!TryRequiredSamples(item.SampleCount, out var required, out var sampleError))
            {
                item.JudgmentResult = null;
                if (strict) errors[field] = [$"The frozen {sampleError}"];
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
        out (string Op, decimal Value)? upper, out string? error) =>
        TryLimits(item.LowerLimitOperator, item.LowerLimitValue, item.UpperLimitOperator,
            item.UpperLimitValue, out lower, out upper, out error);

    private static bool TryLimits(string? lowerOperator, string? lowerValue, string? upperOperator,
        string? upperValue, out (string Op, decimal Value)? lower,
        out (string Op, decimal Value)? upper, out string? error)
    {
        lower = null;
        upper = null;
        error = null;
        if (!string.IsNullOrWhiteSpace(lowerOperator) || !string.IsNullOrWhiteSpace(lowerValue))
        {
            if (lowerOperator is not (">" or ">=") ||
                !decimal.TryParse(lowerValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                error = "The frozen lower limit requires > or >= and a numeric value.";
            else lower = (lowerOperator, value);
        }
        if (!string.IsNullOrWhiteSpace(upperOperator) || !string.IsNullOrWhiteSpace(upperValue))
        {
            if (upperOperator is not ("<" or "<=") ||
                !decimal.TryParse(upperValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                error = "The frozen upper limit requires < or <= and a numeric value.";
            else upper = (upperOperator, value);
        }
        if (lower is null && upper is null && error is null) error = "The frozen Quantitative item has no usable numeric limits.";
        if (lower is { } lo && upper is { } hi &&
            (lo.Value > hi.Value || lo.Value == hi.Value && (lo.Op == ">" || hi.Op == "<")))
            error = "The lower and upper limits have no valid common value.";
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
