using System.Text.Json;
using Relay.Domain.Execution;

namespace Relay.Infrastructure.Execution;

/// <summary>
/// Default in-process adapter: it never makes a real external call. It produces
/// a deterministic success message per connector/action. A step whose config
/// JSON contains <c>"fail": true</c> returns a failure — useful for exercising
/// the retry / failed-run paths without any network.
/// </summary>
public sealed class SimulatedActionDispatcher : IActionDispatcher
{
    public Task<StepExecutionResult> DispatchAsync(StepExecutionRequest request, CancellationToken ct = default)
    {
        if (WantsFailure(request.StepConfigJson))
        {
            return Task.FromResult(StepExecutionResult.Fail(
                $"Simulated failure for {request.ConnectorKey}.{request.Action}"));
        }

        var output = request.ConnectorKey switch
        {
            "http" => $"HTTP {request.Action} dispatched",
            "slack" => "Message posted to Slack",
            "email" => "Email queued",
            "sheets" => "Row appended",
            "delay" => "Delay elapsed",
            _ => $"Executed {request.ConnectorKey}.{request.Action}",
        };

        return Task.FromResult(StepExecutionResult.Ok(output));
    }

    private static bool WantsFailure(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("fail", out var fail)
                && fail.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
