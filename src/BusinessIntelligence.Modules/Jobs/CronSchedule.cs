using System.Globalization;

namespace BusinessIntelligence.Modules.Jobs;

/// <summary>
/// A five-field cron expression (minute hour day-of-month month day-of-week) with <c>*</c>, lists, ranges and steps,
/// read in one time zone. When both day fields are restricted, a day matches either of them, as in cron.
/// </summary>
public sealed class CronSchedule
{
    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _days;
    private readonly bool[] _months;
    private readonly bool[] _weekdays;
    private readonly bool _anyDay;
    private readonly bool _anyWeekday;

    private CronSchedule(string expression, bool[][] fields, bool anyDay, bool anyWeekday)
    {
        Expression = expression;
        (_minutes, _hours, _days, _months, _weekdays) = (fields[0], fields[1], fields[2], fields[3], fields[4]);
        _anyDay = anyDay;
        _anyWeekday = anyWeekday;
    }

    public string Expression { get; }

    public static CronSchedule Parse(string expression)
    {
        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            throw new FormatException($"'{expression}' needs five fields: minute hour day month weekday.");
        }

        (int Min, int Max)[] ranges = [(0, 59), (0, 23), (1, 31), (1, 12), (0, 7)];
        var fields = parts.Select((part, index) => ParseField(part, ranges[index].Min, ranges[index].Max, expression)).ToArray();
        // Sunday is 0 or 7.
        fields[4][0] |= fields[4][7];
        return new CronSchedule(expression, fields, parts[2] == "*", parts[4] == "*");
    }

    /// <summary>The first matching minute strictly after <paramref name="after"/>, within about four years.</summary>
    public DateTimeOffset? Next(DateTimeOffset after, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        var candidate = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
        var limit = candidate.AddYears(4);
        while (candidate < limit)
        {
            if (!_months[candidate.Month] || !DayMatches(candidate))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            if (!_hours[candidate.Hour])
            {
                candidate = candidate.Date.AddHours(candidate.Hour + 1);
                continue;
            }

            if (!_minutes[candidate.Minute])
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            // A local time skipped by a clock change does not exist; the next existing minute is taken.
            if (zone.IsInvalidTime(candidate))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate)).ToUniversalTime();
        }

        return null;
    }

    private bool DayMatches(DateTime day)
    {
        var dayOfMonth = _days[day.Day];
        var weekday = _weekdays[(int)day.DayOfWeek];
        return (_anyDay, _anyWeekday) switch
        {
            (true, true) => true,
            (true, false) => weekday,
            (false, true) => dayOfMonth,
            _ => dayOfMonth || weekday,
        };
    }

    private static bool[] ParseField(string field, int min, int max, string expression)
    {
        var values = new bool[max + 1];
        foreach (var item in field.Split(','))
        {
            var (range, step) = item.Split('/') switch
            {
                [var r] => (r, 1),
                [var r, var s] when int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 => (r, n),
                _ => throw Invalid(expression, item),
            };
            var (from, to) = range == "*" ? (min, max) : range.Split('-') switch
            {
                [var single] => (Number(single), step > 1 ? max : Number(single)),
                [var first, var last] => (Number(first), Number(last)),
                _ => throw Invalid(expression, item),
            };
            if (from < min || to > max || from > to)
            {
                throw Invalid(expression, item);
            }

            for (var value = from; value <= to; value += step)
            {
                values[value] = true;
            }

            int Number(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw Invalid(expression, item);
        }

        return values;
    }

    private static FormatException Invalid(string expression, string item) => new($"'{item}' is not a valid field in '{expression}'.");
}
