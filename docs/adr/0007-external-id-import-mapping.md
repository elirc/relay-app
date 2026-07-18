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
