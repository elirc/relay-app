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
