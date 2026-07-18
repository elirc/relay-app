# 0009 — Optimistic concurrency on flows

**Status:** Accepted

## Context

A flow is a shared, editable resource: two admins (or two browser tabs) can load
the same flow and save conflicting edits. Last-write-wins would silently discard
one person's changes, and because an update **replaces the whole step list**, a lost
update is a real data-loss event, not a cosmetic race.

## Decision

Add `Flow.ConcurrencyToken` (a GUID) marked as an EF concurrency token and rotated
on every successful update. The client reads it as `concurrencyToken` and echoes it
back as `expectedConcurrencyToken` on `PUT`. The controller rejects a mismatch with
**409 before doing any work**, and also maps a `DbUpdateConcurrencyException` (a
writer that beat us between load and save) to **409**. A losing writer applies
nothing.

## Consequences

- Concurrent edits fail loudly (409 → "changed elsewhere, reload and retry") instead
  of silently clobbering.
- A rejected update is fully atomic: the tests confirm none of the losing writer's
  steps leak into the persisted flow.
- Clients must carry the token through the edit round trip; the flow editor surfaces
  the 409 with reconciliation guidance.
- The check is per-flow; it doesn't coordinate edits to unrelated resources.
