using System.Globalization;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public static class PatrolSchedule
{
    public const string PlantTimeZone = "America/New_York";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Weekdays = Enum.GetNames<DayOfWeek>();

    public static string Normalize(ScheduleInput? input, Dictionary<string, string[]> errors)
    {
        if (input is null) { errors["Schedule"] = ["A schedule is required."]; return ""; }
        ScheduleInput normalized;
        switch (input.Type)
        {
            case "everyNHours" when input.IntervalHours is >= 1 and <= 168:
                normalized = new("everyNHours", input.IntervalHours, null, null);
                break;
            case "daily":
            case "weekly":
                var times = (input.Times ?? []).Select(x => x?.Trim() ?? "").Distinct().Order(StringComparer.Ordinal).ToList();
                if (times.Count == 0 || times.Count > 24 || times.Any(x => !TimeOnly.TryParseExact(x, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                { errors["Schedule"] = ["Select one or more valid HH:mm times (up to 24)."]; return ""; }
                var days = input.Type == "weekly" ? (input.DaysOfWeek ?? []).Distinct().OrderBy(x => Array.IndexOf(Weekdays, x)).ToList() : null;
                if (input.Type == "weekly" && (days!.Count == 0 || days.Any(x => !Weekdays.Contains(x))))
                { errors["Schedule"] = ["Select one or more valid weekdays."]; return ""; }
                normalized = new(input.Type, null, times, days);
                break;
            default:
                errors["Schedule"] = ["Use Every N Hours (1–168), Daily, or Weekly."];
                return "";
        }
        return JsonSerializer.Serialize(normalized, Json);
    }

    public static ScheduleInput Read(string json) => JsonSerializer.Deserialize<ScheduleInput>(json, Json)!;

    public static bool TryLocal(string? text, TimeZoneInfo zone, out DateTime utc)
    {
        utc = default;
        if (!DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) return false;
        utc = ToUtc(local, zone);
        return true;
    }

    public static string LocalText(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        // The larger UTC offset is the earlier real instant during the autumn overlap.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    public static IReadOnlyList<DateTime> Occurrences(ScheduleInput schedule, string timeZoneId,
        DateTime effectiveStartUtc, DateTime lowerUtc, DateTime upperUtc)
    {
        if (upperUtc < lowerUtc) return [];
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var anchor = TimeZoneInfo.ConvertTimeFromUtc(effectiveStartUtc, zone);
        var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(lowerUtc, zone).Date.AddDays(-1);
        var toLocal = TimeZoneInfo.ConvertTimeFromUtc(upperUtc, zone).Date.AddDays(1);
        var result = new SortedSet<DateTime>();
        void Add(DateTime local)
        {
            if (local < anchor || zone.IsInvalidTime(local)) return;
            var utc = ToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
            if (utc >= effectiveStartUtc && utc >= lowerUtc && utc <= upperUtc) result.Add(utc);
        }
        if (schedule.Type == "everyNHours")
        {
            var interval = schedule.IntervalHours!.Value;
            var first = Math.Max(0, (long)Math.Floor((fromLocal - anchor).TotalHours / interval));
            for (var index = first; ; index++)
            {
                var candidate = anchor.AddHours(index * interval);
                if (candidate > toLocal.AddDays(1)) break;
                Add(candidate);
            }
        }
        else
        {
            for (var date = fromLocal; date <= toLocal; date = date.AddDays(1))
            {
                if (schedule.Type == "weekly" && !(schedule.DaysOfWeek ?? []).Contains(date.DayOfWeek.ToString())) continue;
                foreach (var time in schedule.Times ?? [])
                    Add(date.Add(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture).ToTimeSpan()));
            }
        }
        return result.ToList();
    }
}
