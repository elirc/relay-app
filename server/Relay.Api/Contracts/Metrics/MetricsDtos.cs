using Relay.Domain.Metrics;

namespace Relay.Api.Contracts.Metrics;

/// <summary>Per-flow metrics row in the workspace dashboard.</summary>
public sealed record FlowMetricsRow(Guid FlowId, string FlowName, MetricsSummary Summary);

/// <summary>Workspace-level dashboard: overall summary, per-flow breakdown, runs over time.</summary>
public sealed record WorkspaceMetricsDto(
    int Days,
    MetricsSummary Overall,
    IReadOnlyList<FlowMetricsRow> PerFlow,
    IReadOnlyList<TimeBucket> RunsOverTime);

/// <summary>Single-flow metrics: summary + runs over time.</summary>
public sealed record FlowMetricsDto(
    Guid FlowId,
    string FlowName,
    int Days,
    MetricsSummary Summary,
    IReadOnlyList<TimeBucket> RunsOverTime);
