using Relay.Domain.Scheduling;

namespace Relay.Tests;

public sealed class CronExpressionTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) =>
        new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]        // too few fields
    [InlineData("* * * * * *")]    // too many fields
    [InlineData("60 * * * *")]     // minute out of range
    [InlineData("* 24 * * *")]     // hour out of range
    [InlineData("* * 0 * *")]      // day-of-month below 1
    [InlineData("*/0 * * * *")]    // zero step
    [InlineData("a * * * *")]      // not a number
    public void TryParse_InvalidExpressions_ReturnFalse(string expr)
    {
        Assert.False(CronExpression.TryParse(expr, out _));
    }

    [Fact]
    public void EveryFiveMinutes_ComputesNext()
    {
        Assert.True(CronExpression.TryParse("*/5 * * * *", out var cron));
        var next = cron!.GetNextOccurrence(Utc(2026, 6, 1, 12, 2));
        Assert.Equal(Utc(2026, 6, 1, 12, 5), next);
    }

    [Fact]
    public void DailyMidnight_RollsToNextDay()
    {
        Assert.True(CronExpression.TryParse("0 0 * * *", out var cron));
        var next = cron!.GetNextOccurrence(Utc(2026, 6, 1, 12, 0));
        Assert.Equal(Utc(2026, 6, 2, 0, 0), next);
    }

    [Fact]
    public void DayOfWeek_MondayMorning()
    {
        // 2026-06-01 is a Monday; 09:30 Monday.
        Assert.True(CronExpression.TryParse("30 9 * * 1", out var cron));
        var next = cron!.GetNextOccurrence(Utc(2026, 6, 1, 0, 0));
        Assert.Equal(Utc(2026, 6, 1, 9, 30), next);
    }

    [Fact]
    public void DayOfWeek_SevenIsSunday()
    {
        Assert.True(CronExpression.TryParse("0 12 * * 7", out var cron));
        var next = cron!.GetNextOccurrence(Utc(2026, 6, 1, 0, 0)); // Mon -> next Sunday
        Assert.Equal(DayOfWeek.Sunday, next!.Value.DayOfWeek);
        Assert.Equal(12, next.Value.Hour);
    }

    [Fact]
    public void GetNextOccurrences_ReturnsRequestedCount_InAscendingOrder()
    {
        Assert.True(CronExpression.TryParse("0 * * * *", out var cron)); // top of every hour
        var runs = cron!.GetNextOccurrences(Utc(2026, 6, 1, 12, 30), 3);
        Assert.Equal(3, runs.Count);
        Assert.Equal(Utc(2026, 6, 1, 13, 0), runs[0]);
        Assert.Equal(Utc(2026, 6, 1, 14, 0), runs[1]);
        Assert.Equal(Utc(2026, 6, 1, 15, 0), runs[2]);
    }

    [Fact]
    public void RangeAndList_AreHonoured()
    {
        Assert.True(CronExpression.TryParse("0 9-17 * * 1-5", out var cron));
        // Saturday 2026-06-06 10:00 -> next weekday (Mon 2026-06-08) 09:00
        var next = cron!.GetNextOccurrence(Utc(2026, 6, 6, 10, 0));
        Assert.Equal(Utc(2026, 6, 8, 9, 0), next);
    }
}
