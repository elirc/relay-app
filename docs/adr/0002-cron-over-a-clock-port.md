# 0002 — Cron scheduling over a clock port

**Status:** Accepted

## Context

Scheduled flows fire on cron expressions. Time-dependent code that reads
`DateTimeOffset.UtcNow` directly is untestable without sleeping or mocking the
system clock, and "did it fire once at the right time?" is exactly the behavior
worth testing.

## Decision

Split scheduling into three pieces and route all time through an `IClock` port:

- `CronExpression` — a pure 5-field parser + `GetNextOccurrence` (minute-by-minute
  search in UTC, bounded horizon, Vixie day-of-month/day-of-week semantics).
- `ScheduleDispatcher` — the testable core: loads due schedules
  (`NextRunAtUtc <= clock.UtcNow`), advances each schedule **before** running its
  flow, then dispatches through `IFlowExecutor`. Constructed with an `IClock`.
- `ScheduleHostedService` — a `BackgroundService` that ticks the dispatcher every
  30s, registered **only outside the test host**.

`SystemClock` backs the app; `FakeClock` (settable/advanceable) backs tests.

## Consequences

- Scheduling tests run instantly and deterministically: set the clock, call
  `RunDueSchedulesAsync`, assert the run count and the re-armed `NextRunAtUtc`.
- Advancing first means a mis-firing flow can't wedge a schedule, and a schedule
  that missed many slots fires **once** per tick and catches up to a future slot.
- The same `IClock` makes webhook timestamp windows and metrics windows testable.
- Cron is evaluated in pure UTC — DST is a display concern, not a scheduling one.
