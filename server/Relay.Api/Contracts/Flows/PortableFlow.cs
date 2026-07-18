using System.Text.Json;
using Relay.Domain.Entities;

namespace Relay.Api.Contracts.Flows;

/// <summary>A template step, described by connector key (not a connection).</summary>
public sealed record FlowTemplateStep(
    string Name,
    string ConnectorKey,
    string Action,
    string ConfigJson,
    int MaxAttempts,
    int BackoffSeconds);

public sealed record FlowTemplateDto(
    Guid Id,
    string Name,
    string Description,
    string Category,
    string TriggerConnectorKey,
    IReadOnlyList<FlowTemplateStep> Steps)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static FlowTemplateDto From(FlowTemplate t)
    {
        var steps = JsonSerializer.Deserialize<List<FlowTemplateStep>>(t.StepsJson, Json) ?? [];
        return new FlowTemplateDto(t.Id, t.Name, t.Description, t.Category, t.TriggerConnectorKey, steps);
    }
}

// ---- Export / import (portable JSON, references by connector key + connection name) ----

public sealed record PortableTrigger(string ConnectorKey, string ConnectionName);

public sealed record PortableStep(
    string Name,
    string ConnectorKey,
    string ConnectionName,
    string Action,
    string ConfigJson,
    int MaxAttempts,
    int BackoffSeconds);

public sealed record FlowExportDto(
    string ExternalId,
    string Name,
    string? Description,
    PortableTrigger Trigger,
    IReadOnlyList<PortableStep> Steps);

/// <summary>Result of a flow import (or dry-run): what would/did happen and any issues.</summary>
public sealed record ImportResultDto(bool Valid, string Action, Guid? FlowId, IReadOnlyList<string> Issues);
