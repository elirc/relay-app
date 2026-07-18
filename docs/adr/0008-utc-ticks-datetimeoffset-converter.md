# 0008 — UTC-ticks converter for DateTimeOffset on SQLite

**Status:** Accepted

## Context

The domain uses `DateTimeOffset` throughout (run timestamps, schedule next-runs,
delivery times), and many queries **order by** or **range over** them
(`OrderByDescending(r => r.StartedAtUtc)`, `NextRunAtUtc <= now`). SQLite has no
native `DateTimeOffset` type and its default text storage does not sort correctly
as instants, so range/order queries would silently return wrong results.

## Decision

Register a global value converter, `DateTimeOffsetToUtcTicksConverter`, that stores
every `DateTimeOffset` as `UtcTicks` (a `long`) and reads it back as a UTC
`DateTimeOffset`. UTC ticks are monotonic with the instant, so `ORDER BY` and range
predicates are correct. It is applied via `ConfigureConventions` so every
`DateTimeOffset` property is covered without per-property wiring. Enums are likewise
stored as **strings** so reordering enum members can't corrupt existing rows.

## Consequences

- Ordering and range queries over time columns are correct on SQLite.
- Round-tripped values are always UTC with a zero offset — the app is UTC-internally,
  which also keeps cron and metrics reasoning simple.
- Sub-tick precision beyond `DateTimeOffset` ticks isn't a concern here; the
  converter is lossless for the values the app stores.
- A dedicated `DateTimeOffsetOrderingTests` suite pins the ordering behavior so a
  future storage change can't regress it silently.
