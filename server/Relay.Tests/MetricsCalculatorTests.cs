using Relay.Domain.Enums;
using Relay.Domain.Metrics;

namespace Relay.Tests;

public sealed class MetricsCalculatorTests
{
    private static RunPoint Run(RunStatus status, DateOnly date, long durationMs) =>
        new(status, new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), durationMs);

    [Fact]
    public void Summarize_Empty_ReturnsZeros()
    {
        var s = MetricsCalculator.Summarize(Array.Empty<RunPoint>());
        Assert.Equal(0, s.TotalRuns);
        Assert.Equal(0, s.SuccessRate);
        Assert.Equal(0, s.P50DurationMs);
    }

    [Fact]
    public void Summarize_ComputesCountsAndSuccessRate()
    {
        var day = new DateOnly(2026, 6, 1);
        var runs = new[]
        {
            Run(RunStatus.Succeeded, day, 10),
            Run(RunStatus.Succeeded, day, 20),
            Run(RunStatus.Succeeded, day, 30),
            Run(RunStatus.Failed, day, 40),
            Run(RunStatus.Failed, day, 50),
        };

        var s = MetricsCalculator.Summarize(runs);

        Assert.Equal(5, s.TotalRuns);
        Assert.Equal(3, s.Succeeded);
        Assert.Equal(2, s.Failed);
        Assert.Equal(0.6, s.SuccessRate);
    }

    [Fact]
    public void Percentile_NearestRank()
    {
        var sorted = new long[] { 10, 20, 30, 40, 50 };
        Assert.Equal(30, MetricsCalculator.Percentile(sorted, 0.50));
        Assert.Equal(50, MetricsCalculator.Percentile(sorted, 0.95));
        Assert.Equal(10, MetricsCalculator.Percentile(sorted, 0.0));
    }

    [Fact]
    public void OverTime_FillsMissingDays_AndBucketsByDate()
    {
        var from = new DateOnly(2026, 6, 1);
        var runs = new[]
        {
            Run(RunStatus.Succeeded, new DateOnly(2026, 6, 1), 10),
            Run(RunStatus.Failed, new DateOnly(2026, 6, 1), 20),
            Run(RunStatus.Succeeded, new DateOnly(2026, 6, 3), 30),
        };

        var buckets = MetricsCalculator.OverTime(runs, from, 3);

        Assert.Equal(3, buckets.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), buckets[0].Date);
        Assert.Equal(2, buckets[0].Total);
        Assert.Equal(1, buckets[0].Succeeded);
        Assert.Equal(1, buckets[0].Failed);
        Assert.Equal(0, buckets[1].Total); // 2026-06-02 has no runs
        Assert.Equal(1, buckets[2].Total);
    }
}
