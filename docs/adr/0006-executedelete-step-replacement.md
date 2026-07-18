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
