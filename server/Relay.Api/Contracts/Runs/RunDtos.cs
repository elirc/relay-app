using System.ComponentModel.DataAnnotations;
using Relay.Domain.Entities;
using Relay.Domain.Enums;

namespace Relay.Api.Contracts.Runs;

public sealed record RunStepLogDto(
    Guid Id,
    int StepOrder,
    string Name,
    RunStatus Status,
    string? Message,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long DurationMs)
{
    public static RunStepLogDto From(RunStepLog l) =>
        new(l.Id, l.StepOrder, l.Name, l.Status, l.Message, l.StartedAtUtc, l.CompletedAtUtc, l.DurationMs);
}

public sealed record RunSummaryDto(
    Guid Id,
    Guid FlowId,
    string FlowName,
    RunStatus Status,
    RunTrigger Trigger,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long DurationMs,
    int RetryCount)
{
    public static RunSummaryDto From(Run r) =>
        new(r.Id, r.FlowId, r.Flow?.Name ?? string.Empty, r.Status, r.Trigger,
            r.StartedAtUtc, r.CompletedAtUtc, r.DurationMs, r.RetryCount);
}

public sealed record RunDetailDto(
    Guid Id,
    Guid FlowId,
    string FlowName,
    RunStatus Status,
    RunTrigger Trigger,
    string? Error,
    string? TriggerPayloadJson,
    string? IdempotencyKey,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long DurationMs,
    int RetryCount,
    IReadOnlyList<RunStepLogDto> StepLogs)
{
    public static RunDetailDto From(Run r) =>
        new(r.Id, r.FlowId, r.Flow?.Name ?? string.Empty, r.Status, r.Trigger, r.Error,
            r.TriggerPayloadJson, r.IdempotencyKey, r.StartedAtUtc, r.CompletedAtUtc, r.DurationMs, r.RetryCount,
            r.StepLogs.OrderBy(l => l.StepOrder).Select(RunStepLogDto.From).ToList());
}

/// <summary>Optional payload for a manual run.</summary>
public sealed record TriggerRunRequest(string? PayloadJson);

/// <summary>Replay a run, optionally skipping the action steps before <c>FromStepOrder</c>.</summary>
public sealed record ReplayRunRequest([Range(0, 1000)] int? FromStepOrder);
