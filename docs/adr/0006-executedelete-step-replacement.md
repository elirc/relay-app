# 0006 — ExecuteDeleteAsync step replacement in a transaction

**Status:** Accepted

## Context

A flow's steps are an **ordered** list with a unique `(FlowId, Order)` index.
Editing a flow replaces the whole list. A naive "diff and update in place" risks two
rows transiently sharing an `Order` mid-save (a unique-index violation), and change
tracking the deletes/inserts together makes ordering fragile.

## Decision

Replace the step list wholesale inside a single transaction: `ExecuteDeleteAsync`
issues a direct `DELETE` for the flow's steps (bypassing the change tracker), then
the new steps are inserted with fresh sequential orders, and the transaction
commits. Flow **import** (update branch) uses the identical pattern.

## Consequences

- The unique `(FlowId, Order)` index never sees a conflicting intermediate state.
- The operation is atomic — a failure rolls back to the original steps.
- `ExecuteDeleteAsync` doesn't load rows into memory, so replacement is cheap
  regardless of step count.
- Because it bypasses the change tracker, the surrounding save/commit ordering must
  be explicit (delete → insert → save → commit), which the controller does.

## Where it lives in the code

- Update: `server/Relay.Api/Controllers/FlowsController.cs:119-131` — transaction (`:121`), `ExecuteDeleteAsync` (`:122`), `AddRange` (`:123`), save (`:124`), commit (`:125`). On `DbUpdateConcurrencyException` the transaction is disposed uncommitted, so the delete rolls back too.
- Import update branch: `:324-328`, same sequence.
- Index: `server/Relay.Infrastructure/Persistence/RelayDbContext.cs:130`.

## Known gaps

- Step ids are regenerated on every save, so anything that references a step by id would break; run logs reference steps by `StepOrder`, which is why this is safe today.
- The import branch does not catch `DbUpdateConcurrencyException`, unlike the update path.
