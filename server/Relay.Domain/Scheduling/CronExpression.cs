using System.Globalization;

namespace Relay.Domain.Scheduling;

/// <summary>
/// A standard 5-field cron expression (minute hour day-of-month month
/// day-of-week) supporting <c>*</c>, numbers, lists (<c>,</c>), ranges
/// (<c>a-b</c>) and steps (<c>*/n</c>, <c>a-b/n</c>). Day-of-week accepts 0 or 7
/// for Sunday. Next-occurrence uses Vixie semantics: when both day-of-month and
/// day-of-week are restricted, a day matches if <em>either</em> matches.
/// </summary>
public sealed class CronExpression
{
    // Bounded forward search so a never-matching expression can't loop forever.
    private const int SearchLimitDays = 1500;

    private readonly bool[] _minutes;   // 0-59
    private readonly bool[] _hours;     // 0-23
    private readonly bool[] _daysOfMonth; // 1-31
    private readonly bool[] _months;    // 1-12
    private readonly bool[] _daysOfWeek; // 0-6 (Sunday = 0)
    private readonly bool _domRestricted;
    private readonly bool _dowRestricted;

    public string Expression { get; }

    private CronExpression(
        string expression,
        bool[] minutes, bool[] hours, bool[] daysOfMonth, bool[] months, bool[] daysOfWeek,
        bool domRestricted, bool dowRestricted)
    {
        Expression = expression;
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
        _domRestricted = domRestricted;
        _dowRestricted = dowRestricted;
    }

    public static bool TryParse(string? expression, out CronExpression? cron)
    {
        cron = null;
        if (string.IsNullOrWhiteSpace(expression)) return false;

        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) return false;

        try
        {
            var minutes = ParseField(fields[0], 0, 59, out _);
            var hours = ParseField(fields[1], 0, 23, out _);
            var daysOfMonth = ParseField(fields[2], 1, 31, out var domRestricted);
            var months = ParseField(fields[3], 1, 12, out _);
            var daysOfWeek = ParseDayOfWeek(fields[4], out var dowRestricted);

            cron = new CronExpression(
                expression.Trim(), minutes, hours, daysOfMonth, months, daysOfWeek,
                domRestricted, dowRestricted);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The next occurrence strictly after <paramref name="after"/> (UTC), or null.</summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
    {
        var start = after.UtcDateTime;
        var candidate = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1);
        var limit = candidate.AddDays(SearchLimitDays);

        while (candidate < limit)
        {
            if (Matches(candidate)) return new DateTimeOffset(candidate, TimeSpan.Zero);
            candidate = candidate.AddMinutes(1);
        }
        return null;
    }

    /// <summary>The next <paramref name="count"/> occurrences after <paramref name="after"/>.</summary>
    public IReadOnlyList<DateTimeOffset> GetNextOccurrences(DateTimeOffset after, int count)
    {
        var results = new List<DateTimeOffset>();
        var cursor = after;
        for (var i = 0; i < count; i++)
        {
            var next = GetNextOccurrence(cursor);
            if (next is null) break;
            results.Add(next.Value);
            cursor = next.Value;
        }
        return results;
    }

    private bool Matches(DateTime dt)
    {
        if (!_months[dt.Month - 1] || !_hours[dt.Hour] || !_minutes[dt.Minute]) return false;

        var domOk = _daysOfMonth[dt.Day - 1];
        var dowOk = _daysOfWeek[(int)dt.DayOfWeek];
        var dayOk = _domRestricted && _dowRestricted ? domOk || dowOk : domOk && dowOk;
        return dayOk;
    }

    private static bool[] ParseField(string field, int min, int max, out bool restricted)
    {
        restricted = field.Trim() != "*";
        var set = new bool[max - min + 1];

        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var stepSplit = part.Split('/');
            if (stepSplit.Length > 2) throw new FormatException();
            var step = stepSplit.Length == 2 ? ParseInt(stepSplit[1]) : 1;
            if (step < 1) throw new FormatException();

            var range = stepSplit[0];
            int lo, hi;
            if (range == "*")
            {
                lo = min;
                hi = max;
            }
            else if (range.Contains('-'))
            {
                var bounds = range.Split('-');
                if (bounds.Length != 2) throw new FormatException();
                lo = ParseInt(bounds[0]);
                hi = ParseInt(bounds[1]);
            }
            else
            {
                lo = hi = ParseInt(range);
            }

            if (lo < min || hi > max || lo > hi) throw new FormatException();
            for (var v = lo; v <= hi; v += step) set[v - min] = true;
        }

        return set;
    }

    private static bool[] ParseDayOfWeek(string field, out bool restricted)
    {
        // Parse over 0-7 (7 = Sunday), then fold 7 into 0.
        var raw = ParseField(field, 0, 7, out restricted);
        var set = new bool[7];
        for (var i = 0; i <= 6; i++) set[i] = raw[i];
        if (raw[7]) set[0] = true;
        return set;
    }

    private static int ParseInt(string s) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new FormatException();
}
