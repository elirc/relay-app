using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Execution;
using Relay.Infrastructure.Persistence;

namespace Relay.Infrastructure.Execution;

/// <summary>
/// Orchestrates a flow run over the <see cref="IActionDispatcher"/> port: runs
/// each step in order, retries a failing step up to <see cref="MaxAttempts"/>,
/// skips the rest after a hard failure, and persists a <see cref="Run"/> with a
/// per-step log timeline.
/// </summary>
public sealed class FlowExecutor : IFlowExecutor
{
    public const int MaxAttempts = 3;

    private readonly RelayDbContext _db;
    private readonly IActionDispatcher _dispatcher;

    public FlowExecutor(RelayDbContext db, IActionDispatcher dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
    }

    public async Task<Run?> RunFlowAsync(
        Guid flowId,
        RunTrigger trigger,
        string? payloadJson,
        CancellationToken ct = default)
    {
        var flow = await _db.Flows
            .Include(f => f.TriggerConnection)
            .Include(f => f.Steps).ThenInclude(s => s.Connection).ThenInclude(c => c!.Connector)
            .FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow is null) return null;

        var startedAt = DateTimeOffset.UtcNow;
        var run = new Run
        {
            Id = Guid.NewGuid(),
            FlowId = flow.Id,
            Status = RunStatus.Running,
            Trigger = trigger,
            TriggerPayloadJson = payloadJson,
            StartedAtUtc = startedAt,
        };
        _db.Runs.Add(run);

        // Step 0 is the trigger itself, always "succeeded" once we get here.
        run.StepLogs.Add(new RunStepLog
        {
            Id = Guid.NewGuid(),
            StepOrder = 0,
            Name = $"Trigger: {flow.TriggerConnection?.Name ?? "unknown"}",
            Status = RunStatus.Succeeded,
            Message = $"Flow triggered ({trigger}).",
            StartedAtUtc = startedAt,
            CompletedAtUtc = startedAt,
            DurationMs = 0,
        });

        var failed = false;
        var totalRetries = 0;

        foreach (var step in flow.Steps.OrderBy(s => s.Order))
        {
            var order = step.Order + 1;

            if (failed)
            {
                run.StepLogs.Add(new RunStepLog
                {
                    Id = Guid.NewGuid(),
                    FlowStepId = step.Id,
                    StepOrder = order,
                    Name = step.Name,
                    Status = RunStatus.Skipped,
                    Message = "Skipped after an earlier step failed.",
                    StartedAtUtc = DateTimeOffset.UtcNow,
                });
                continue;
            }

            var stepStart = DateTimeOffset.UtcNow;
            var request = new StepExecutionRequest(
                step.Connection?.Connector?.Key ?? string.Empty,
                step.Action,
                step.ConfigJson,
                step.Connection?.ConfigJson ?? "{}",
                payloadJson);

            StepExecutionResult result = StepExecutionResult.Fail("Not executed");
            var attempts = 0;
            while (true)
            {
                attempts++;
                try
                {
                    result = await _dispatcher.DispatchAsync(request, ct);
                }
                catch (Exception ex)
                {
                    result = StepExecutionResult.Fail(ex.Message);
                }

                if (result.Success || attempts >= MaxAttempts) break;
            }
            totalRetries += attempts - 1;

            var stepEnd = DateTimeOffset.UtcNow;
            run.StepLogs.Add(new RunStepLog
            {
                Id = Guid.NewGuid(),
                FlowStepId = step.Id,
                StepOrder = order,
                Name = step.Name,
                Status = result.Success ? RunStatus.Succeeded : RunStatus.Failed,
                Message = BuildMessage(result, attempts),
                StartedAtUtc = stepStart,
                CompletedAtUtc = stepEnd,
                DurationMs = (long)(stepEnd - stepStart).TotalMilliseconds,
            });

            if (!result.Success) failed = true;
        }

        var completedAt = DateTimeOffset.UtcNow;
        run.Status = failed ? RunStatus.Failed : RunStatus.Succeeded;
        run.CompletedAtUtc = completedAt;
        run.DurationMs = (long)(completedAt - startedAt).TotalMilliseconds;
        run.RetryCount = totalRetries;
        run.Error = failed ? "One or more steps failed." : null;

        await _db.SaveChangesAsync(ct);
        return run;
    }

    private static string BuildMessage(StepExecutionResult result, int attempts)
    {
        var attemptNote = attempts > 1 ? $" (after {attempts} attempts)" : string.Empty;
        return result.Success
            ? $"{result.Output}{attemptNote}"
            : $"{result.Error}{attemptNote}";
    }
}
