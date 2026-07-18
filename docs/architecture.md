# Architecture

relay-app is a Zapier-style integrations platform: workspaces install **connectors**
as **connections**, compose them into **flows** (a trigger + ordered action steps),
and execute those flows as **runs** — manually, on a **schedule**, or via inbound
**webhooks** — with per-step logging, retries, a dead-letter queue, metrics, and
envelope-encrypted secrets.

This document explains how the pieces fit together. For the endpoint catalog see
[`api-reference.md`](api-reference.md); for how to run it see
[`getting-started.md`](getting-started.md); for the decision record see
[`adr/`](adr/README.md).

---

## Monorepo layout

| Path      | Stack                                                            |
| --------- | ---------------------------------------------------------------- |
| `/server` | ASP.NET Core Web API, .NET 10, EF Core + SQLite, xUnit           |
| `/client` | Vite + React + TypeScript (strict), React Router, Vitest + RTL   |

### Server projects (dependencies point inward)

```
Relay.Api ──► Relay.Infrastructure ──► Relay.Domain
   (controllers,        (EF Core, migrations,      (entities, enums,
    contracts,           seeder, executor,          ports, pure domain
    auth, DI wiring)      adapters, KMS)             logic — no infra deps)
```

- **`Relay.Domain`** — entities, enums, and the ports the rest of the system is
  written against: `IActionDispatcher` / `IFlowExecutor` (execution), `IClock`
  (time), `IKeyManagementService` / `ISecretProtector` (secrets), `IDelayer`
  (retry backoff). Also pure logic with no dependencies: `CronExpression`,
  `MetricsCalculator`, `WebhookSignature`, `JsonSchemaValidator`.
- **`Relay.Infrastructure`** — `RelayDbContext`, EF Core migrations, the
  `DatabaseSeeder`, the `FlowExecutor`, the in-process `SimulatedActionDispatcher`,
  the `ScheduleDispatcher`, `EnvelopeSecretProtector` + `LocalKeyManagementService`,
  `SystemClock`, and `TaskDelayer`.
- **`Relay.Api`** — controllers, request/response contracts (records with
  validation), the JWT + workspace-authorization pipeline, rate limiting, request
  logging, ProblemDetails, CORS, and startup migrate/seed.
- **`Relay.Tests`** — xUnit unit tests (executor, cron, metrics, secrets over
  in-memory SQLite) and API integration tests over `WebApplicationFactory`.

### Client

A typed `fetch` wrapper (`api/client.ts` — `ApiError`, `ProblemDetails`, bearer
token, 401 handler), per-feature api modules, a `useAsync` data hook, an
`AuthProvider` + `WorkspaceProvider` context pair, and pages for connectors,
connections, flows (list + editor), runs, dead-letter, metrics, templates, and
health. Tests mock the api layer, so **no running server is required**.

---

## Executor & the ports design

Flow execution never makes a real external call. `FlowExecutor` (the sole
`IFlowExecutor`) drives steps through the `IActionDispatcher` **port**:

```
IFlowExecutor.RunFlowAsync(flowId, trigger, payload, ct, fromStepOrder, idempotencyKey)
      │
      ├─ loads the flow (Include trigger + steps + connection + connector)
      ├─ creates a Run (status Running) and logs step 0 = the trigger
      ├─ for each ordered step:
      │     • skip if step.Order < fromStepOrder   (replay)
      │     • skip if a prior step already failed
      │     • else dispatch through IActionDispatcher, retrying up to MaxAttempts
      └─ persists the Run + per-step RunStepLog timeline
```

- **Adapter in the app**: `SimulatedActionDispatcher` returns a deterministic
  success message per connector/action, or a failure when the step config JSON
  contains `"fail": true` — enough to exercise every retry / failure path with no
  network.
- **Adapter in tests**: `FakeActionDispatcher` with a per-request `Handler` and a
  `Calls` counter, so a test can assert exactly which steps were dispatched.

Because the executor depends only on the port, the same code path runs a manual
trigger, a scheduled tick, a webhook delivery, and a dead-letter replay.

### Retry, backoff & dead-letter model

Each `FlowStep` carries a retry policy: `MaxAttempts` (1–10, default 3) and
`BackoffSeconds` (0–3600, default 0). Within a step:

- The dispatcher is called up to `MaxAttempts` times; a thrown exception is caught
  and treated as a step failure (never an unhandled error).
- Between failed attempts the executor waits via the `IDelayer` port
  (`TaskDelayer` in the app; `FakeDelayer` records requested delays without
  sleeping in tests). Backoff applies **between** attempts only — never after the
  final attempt or after a success.
- `Run.RetryCount` accumulates `attempts - 1` across steps.

When a step exhausts its attempts, the run is marked **Failed**, its `Error` is
set, and every later step is logged **Skipped** ("Skipped after an earlier step
failed"). Failed runs surface in the **dead-letter** list
(`GET /api/workspaces/{ws}/dead-letter`).

**Replay** re-runs a failed run from a chosen action step:
`fromStepOrder` skips every earlier action step (logged "Skipped (replay from step
N)") and dispatches from that point onward — so already-completed work isn't
repeated. Replay produces a new `Run`; a replay of a replay is just another run.

---

## Scheduling over the clock port

Cron scheduling is split into a pure parser, a testable dispatcher, and a thin
hosted service:

- **`CronExpression`** — a standard 5-field parser (minute hour day-of-month month
  day-of-week) supporting `*`, numbers, lists (`,`), ranges (`a-b`) and steps
  (`*/n`). Day-of-week accepts `0` or `7` for Sunday. It uses **Vixie semantics**:
  when both day-of-month and day-of-week are restricted, a day matches if
  **either** matches. `GetNextOccurrence` searches minute-by-minute in pure UTC
  with a bounded horizon (so a never-matching expression can't loop forever).
- **`ScheduleDispatcher`** — the testable core. On each tick it loads enabled,
  due schedules (`NextRunAtUtc <= clock.UtcNow`), and for each: advances
  `LastRunAtUtc`/`NextRunAtUtc` **first** (so a mis-firing flow can't wedge the
  schedule), then runs the flow through `IFlowExecutor` if the flow is enabled.
  A schedule that missed many slots fires **once** per tick and re-arms to the
  next future slot.
- **`ScheduleHostedService`** — a `BackgroundService` that ticks the dispatcher
  every 30s in a fresh DI scope. It is registered **only outside the test host**;
  tests drive `ScheduleDispatcher` directly with a `FakeClock` for determinism.

The clock is the `IClock` port (`SystemClock` in the app, `FakeClock` in tests),
so every time-dependent behavior — next-run computation, webhook freshness,
metrics windows — is deterministic under test.

---

## Secrets: envelope encryption + rotation

Connection credentials and webhook signing secrets are **encrypted at rest** with
envelope encryption over the `IKeyManagementService` port:

```
plaintext ─► EnvelopeSecretProtector.Protect
                 │  GenerateDataKey() ──► KMS returns { plaintext DEK, wrapped DEK }
                 │  AES-GCM encrypt(plaintext, DEK)  → nonce, tag, cipher
                 │  zero the plaintext DEK
                 └► envelope JSON { v, wrappedKey, nonce, tag, cipher }
```

- **`LocalKeyManagementService`** (the app's KMS stand-in) wraps each data key
  with AES-GCM under a master key derived (SHA-256) from `Secrets:MasterKey`. It
  never sees secret payloads — only data keys.
- **`FakeKms`** (tests) wraps by XOR against a fixed mask — enough to exercise the
  envelope round trip and rotation with no crypto dependency.
- **Write-only**: `ISecretProtector.Reveal` exists only for internal use (webhook
  signature verification, rotation). Secrets are **never** returned by any
  endpoint — DTOs expose only a boolean (`hasCredentials` / `hasSigningSecret`).
- **Rotation** (`Rotate = Protect(Reveal(envelope))`) re-seals the same plaintext
  under a brand-new data key, so the stored ciphertext changes while the value is
  preserved. `POST .../rotate-secret` re-encrypts a connection's secret;
  re-issuing a webhook signing secret mints a fresh one (old signatures stop
  verifying).

---

## Webhook verification: HMAC + timestamp + idempotency

Inbound webhooks (`POST /api/hooks/{token}`) are public (the unguessable token is
the credential) and optionally hardened with a per-endpoint HMAC signing secret:

1. **Signature** — the caller sends `X-Relay-Timestamp` and `X-Relay-Signature`,
   a lowercase-hex HMAC-SHA256 over `{timestamp}.{body}`. Because the body is part
   of the signed string, a captured request can't be replayed with a different
   body. Verification is constant-time (`CryptographicOperations.FixedTimeEquals`).
   Missing/malformed → `401`.
2. **Timestamp window** — the timestamp must be within ±5 minutes of the clock
   (absolute drift, so mild forward skew is tolerated). Outside the window → `401`
   (anti-replay). The check reads the `IClock` port.
3. **Idempotency** — an `Idempotency-Key` header de-duplicates deliveries: a
   duplicate key for the same flow reuses the original run instead of creating a
   new one (enforced by a unique `(FlowId, IdempotencyKey)` index and an explicit
   lookup). The `202 Accepted` body reports `deduplicated: true` on a replay.

Every attempt is recorded in the webhook's **delivery log**, classified by outcome
(`Delivered`, `MissingSignature`, `InvalidSignature`, `TimestampExpired`,
`FlowDisabled`, `Duplicate`).

---

## Metrics

`MetricsCalculator` is pure: given a set of run projections it computes total /
succeeded / failed counts, **success rate**, **p50/p95** duration (nearest-rank
percentile over the sorted durations), and a continuous day-by-day time series
with missing days zero-filled. `MetricsController` projects runs (workspace-wide
or per-flow) over a clamped window (1–90 days, default 7, window anchored on the
`IClock`) and returns an overall summary, a per-flow breakdown, and the series for
the dashboard.

---

## Concurrency model

- **Optimistic concurrency on flows** — `Flow.ConcurrencyToken` is an EF concurrency
  token, rotated to a fresh GUID on every update. `PUT` accepts an
  `expectedConcurrencyToken`; a mismatch short-circuits to **409** before any work,
  and a `DbUpdateConcurrencyException` (a writer that beat us between load and save)
  also maps to **409**. A losing writer applies **nothing** — no partial step
  replacement leaks through.
- **Ordered step replacement** — a flow's steps are replaced wholesale inside a
  transaction: `ExecuteDeleteAsync` removes the old rows (bypassing the change
  tracker) and the new rows are inserted, so the unique `(FlowId, Order)` index
  never sees two rows share an order mid-save. Import uses the same pattern.
- **Idempotency** — webhook runs are de-duplicated by a unique
  `(FlowId, IdempotencyKey)` index; flow import is idempotent by a unique
  `(WorkspaceId, ExternalId)` index (create vs update).
- **Rate limiting** — trigger endpoints (manual run + inbound webhook) share a
  fixed-window limiter (1000 requests/60s by default, configurable via
  `RateLimiting:TriggerPermitLimit`). Over the limit → **429**; non-trigger routes
  are unaffected.

---

## Cross-cutting notes

- **SQLite + DateTimeOffset** — SQLite can't order/compare `DateTimeOffset`, so
  `DateTimeOffsetToUtcTicksConverter` stores them as UTC ticks (`long`), which sort
  identically to the original instants. Enums persist as **strings** so reordering
  members is safe.
- **Record-DTO validation** — validation attributes target **constructor
  parameters**; value-type request fields are **nullable** so a missing value is a
  `400`, not a silent bind to `default`.
- **EF gotchas** — `Include` before `Skip`/`Take`; client-keyed rows reached only
  via a tracked parent are added through the `DbSet` so EF marks them `Added`, not
  `Modified`.
- **Auth pipeline** — JWT bearer with a fallback "require authenticated user"
  policy; a global `WorkspaceAuthorizationFilter` enforces tenancy (foreign
  workspace → **404**, before role) and role (`RequireWorkspaceRole` → **403**).
- **No OpenAPI package** — Microsoft.OpenApi 2.x was removed; the API surface is
  documented here rather than via a generated Swagger document.
