# 0007 — External-id mapping for idempotent import

**Status:** Accepted

## Context

Flows can be exported as portable JSON and imported into another workspace (or the
same one). Re-running an import — from a re-synced source or a retried request —
must not create duplicate flows, and the export must not leak workspace-internal
ids (connection GUIDs) that mean nothing elsewhere.

## Decision

Export references dependencies **by connector key + connection name**, not by id,
and carries a stable `externalId`. Import resolves those references against the
target workspace's connections and upserts by `(WorkspaceId, ExternalId)` — backed
by a unique index. No existing flow with that external id → **create**; one exists →
**update** (steps replaced via [ADR-0006](0006-executedelete-step-replacement.md)).
`?dryRun=true` runs the full validation and reports the action **without persisting**.

## Consequences

- Re-import is idempotent: the same document yields one flow, `create` then
  `update`, with no duplicated flows or steps.
- Documents are portable across workspaces because they name dependencies, not ids;
  an unresolvable connector is a validation issue (dry-run reports it, a real import
  → 400).
- Dry-run gives a safe "what would happen" with zero side effects — verified by the
  tests asserting the flow count and external-id presence are unchanged.
- `externalId` is the contract for identity; two different logical flows must not
  share one.

## Where it lives in the code

`server/Relay.Api/Controllers/FlowsController.cs` — export at `:229` (uses `ExternalId ?? Id`, `:242`); import at `:257-333`: issues collected (`:268-282`), upsert lookup by `(WorkspaceId, ExternalId)` (`:284-286`), dry-run return (`:290-291`), create (`:305-317`; imported flows start **disabled**, `:311`), update (`:318-330`). Unique index: `RelayDbContext.cs:115`. Tests: `TemplatesAndPortabilityApiTests.cs`, `ImportExportExpansionTests.cs`.

## Known gaps

- **Silent fallback binding.** `Resolve` (`:341-343`) first matches connector key + connection name, then falls back to *any* connection of that connector. A document naming "Prod Slack" imports cleanly against "Test Slack" and no issue is reported.
- **Less validation than the editor.** `PUT` runs `ValidateGraphAsync` (`:106`); import does not, so a step `ConfigJson` that violates the connector's schema is accepted (only blank is defaulted to `{}`, `:300`). Attempts and backoff are clamped rather than rejected (`:301`).
- **Concurrency.** The update branch neither checks nor rotates `ConcurrencyToken` (`:320-323`), so an import silently overwrites an admin's in-flight edit, and that admin's later `PUT` with the old token still succeeds. Two concurrent first imports of the same `externalId` race to the unique index and the loser gets a 500.
