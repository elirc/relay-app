using Relay.Domain.Enums;

namespace Relay.Domain.Metrics;

/// <summary>A minimal run projection metrics are computed from.</summary>
public sealed record RunPoint(RunStatus Status, DateTimeOffset StartedAtUtc, long DurationMs);

/// <summary>Aggregate run metrics over a set of runs.</summary>
public sealed record MetricsSummary(
    int TotalRuns,
    int Succeeded,
    int Failed,
    double SuccessRate,
    long P50DurationMs,
    long P95DurationMs);

/// <summary>Run counts for a single day bucket.</summary>
public sealed record TimeBucket(DateOnly Date, int Total, int Succeeded, int Failed);

/// <summary>
/// Pure computation of run metrics: success rate, p50/p95 duration (nearest-rank),
/// and a continuous day-by-day time series (missing days filled with zeros).
/// </summary>
public static class MetricsCalculator
{
    public static MetricsSummary Summarize(IReadOnlyCollection<RunPoint> runs)
    {
        var total = runs.Count;
        if (total == 0) return new MetricsSummary(0, 0, 0, 0, 0, 0);

        var succeeded = runs.Count(r => r.Status == RunStatus.Succeeded);
        var failed = runs.Count(r => r.Status == RunStatus.Failed);
        var durations = runs.Select(r => r.DurationMs).OrderBy(d => d).ToList();

        return new MetricsSummary(
            total,
            succeeded,
            failed,
            Math.Round((double)succeeded / total, 4),
            Percentile(durations, 0.50),
            Percentile(durations, 0.95));
    }

    /// <summary><paramref name="days"/> day buckets starting at <paramref name="from"/>.</summary>
    public static IReadOnlyList<TimeBucket> OverTime(IReadOnlyCollection<RunPoint> runs, DateOnly from, int days)
    {
        var byDate = runs
            .GroupBy(r => DateOnly.FromDateTime(r.StartedAtUtc.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var buckets = new List<TimeBucket>(days);
        for (var i = 0; i < days; i++)
        {
            var date = from.AddDays(i);
            if (byDate.TryGetValue(date, out var dayRuns))
            {
                buckets.Add(new TimeBucket(
                    date,
                    dayRuns.Count,
                    dayRuns.Count(r => r.Status == RunStatus.Succeeded),
                    dayRuns.Count(r => r.Status == RunStatus.Failed)));
            }
            else
            {
                buckets.Add(new TimeBucket(date, 0, 0, 0));
            }
        }
        return buckets;
    }

    /// <summary>Nearest-rank percentile (<paramref name="p"/> in [0,1]) of a sorted list.</summary>
    public static long Percentile(IReadOnlyList<long> sortedAscending, double p)
    {
        var n = sortedAscending.Count;
        if (n == 0) return 0;
        var rank = (int)Math.Ceiling(p * n);
        var index = Math.Clamp(rank - 1, 0, n - 1);
        return sortedAscending[index];
    }
}
