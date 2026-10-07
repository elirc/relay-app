# 0001 — Ports-based flow executor (no real external calls)

**Status:** Accepted

## Context

A flow runs ordered steps that, in a real product, would call external services
(Slack, HTTP, email). Making real calls from tests and demos is slow, flaky, and
requires credentials and network access — none of which belong in CI or a
first-boot demo.

## Decision

Define execution as a **port**: `IActionDispatcher.DispatchAsync(request)` returns
a `StepExecutionResult`. `FlowExecutor` (the `IFlowExecutor`) depends only on that
port. The app supplies `SimulatedActionDispatcher`, which returns a deterministic
success per connector/action and fails when a step's config JSON contains
`"fail": true`. Tests supply `FakeActionDispatcher` with a per-request handler and
a call counter. The `StepExecutionRequest` carries only primitives (connector key,
action, config JSON, connection config, payload) — no EF or HTTP types cross the
port.

## Consequences

- Every execution path — manual run, schedule, webhook, dead-letter replay — runs
  the same code with no network. CI is deterministic and offline.
- Tests assert exactly which steps dispatched (via `Calls`) and can force any
  failure shape, which is what makes retry/skip/replay coverage precise.
- A real adapter (actual HTTP/Slack calls) can be added later behind the same port
  without touching the executor.
- The trade-off: the default experience is simulated, so "it worked in the demo"
  does not prove a real integration works — that is the adapter's responsibility.

## Where it lives in the code

- Port: `IActionDispatcher` / `StepExecutionRequest` in `server/Relay.Domain/Execution/`.
- App adapter: `server/Relay.Infrastructure/Execution/SimulatedActionDispatcher.cs:14` (`DispatchAsync`); the `"fail": true` switch is read at `:42-43`.
- Executor: `server/Relay.Infrastructure/Execution/FlowExecutor.cs` — constructor at `:24` takes the `RelayDbContext`, the dispatcher and an optional `IDelayer`; the per-step retry loop calls the port at `:125` and waits `BackoffSeconds` through the delayer at `:137`; a step's own `MaxAttempts` wins over the default of 3 (`:18`, `:117`).
- Registration: `server/Relay.Infrastructure/DependencyInjection.cs:35-37`.
- Test double: `server/Relay.Tests/Support/FakeActionDispatcher.cs` (`Handler` at `:8`, `Calls` at `:11`).

## Known gaps

- "Depends only on the port" is true for side effects, not for persistence: the executor writes runs directly through `RelayDbContext` and saves once, at the end of the run (`FlowExecutor.cs:166`). A process crash mid-run leaves no `Run` row at all, so there is nothing to resume or dead-letter.
- The executor stamps times with `DateTimeOffset.UtcNow` (`:45`, `:109`, `:142`, `:159`) rather than the `IClock` port from ADR-0002, so run durations cannot be driven by `FakeClock`.
- Runs execute inline in the HTTP request (manual run, webhook) — a slow adapter would hold the caller's connection for every retry and backoff.
