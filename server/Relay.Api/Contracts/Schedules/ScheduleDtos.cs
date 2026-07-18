using System.ComponentModel.DataAnnotations;
using Relay.Domain.Entities;

namespace Relay.Api.Contracts.Schedules;

public sealed record ScheduleDto(
    Guid Id,
    Guid FlowId,
    string CronExpression,
    bool IsEnabled,
    DateTimeOffset? NextRunAtUtc,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ScheduleDto From(Schedule s) =>
        new(s.Id, s.FlowId, s.CronExpression, s.IsEnabled, s.NextRunAtUtc, s.LastRunAtUtc, s.CreatedAtUtc, s.UpdatedAtUtc);
}

/// <summary>Create/update body. Cron is nullable so a missing value is a 400.</summary>
public sealed record ScheduleRequest(
    [Required]
    [StringLength(120, MinimumLength = 1)]
    string? CronExpression);

/// <summary>Validation + a preview of upcoming fire times for a cron expression.</summary>
public sealed record SchedulePreviewResponse(bool Valid, string? Error, IReadOnlyList<DateTimeOffset> NextRuns);
