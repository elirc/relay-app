# relay-app

A Zapier-style **integrations / connectors platform**. Workspaces install
**connectors** (integration types) as **connections** (configured instances with
credentials), compose them into **flows** (a trigger + ordered action steps), and
execute those flows as **runs** — manually or via inbound **webhooks** — with full
per-step logging and retry.

This is a monorepo:

| Path      | Stack                                                             |
| --------- | ----------------------------------------------------------------- |
| `/server` | ASP.NET Core Web API, .NET 10, EF Core + SQLite, xUnit            |
| `/client` | Vite + React + TypeScript (strict), React Router, Vitest + RTL    |

Flow execution runs through an in-process executor over **ports** (interfaces),
so there are **no real external calls** — the default adapter simulates each
connector deterministically, and tests inject fakes.

---

## Domain

```
Workspace ─┬─ User                     (auth: PBKDF2-hashed credentials)
           ├─ Connection ── Connector   (installed instance of a catalog type)
           └─ Flow ─┬─ FlowStep         (ordered action steps)
                    ├─ Run ── RunStepLog (execution history + per-step timeline)
                    └─ Webhook           (inbound endpoint that triggers the flow)
```

- **Connector** — a catalog entry (global): key, name, auth kind, JSON config
  schema. Connectors are **versioned**: each has one or more schema versions, and
  a version can be **deprecated** to steer new installs to a newer one.
- **Connection** — a connector installed in a workspace, with config + stored
  credentials. The config is **validated against the connector version's JSON
  schema** on create/update. Secrets are **encrypted at rest** with envelope
  encryption (a per-secret data key wrapped by a key port / local KMS), are
  **write-only** (never returned — only a `hasCredentials` flag), and can be
  **rotated** to a fresh data key.
- **Flow** — a trigger connection plus an ordered list of steps; enable/disable.
- **Run** — one execution: status (`Pending`/`Running`/`Succeeded`/`Failed`),
  duration, retry count, and a `RunStepLog` per step (trigger is step 0). Each
  step carries a **retry policy** (max attempts + backoff); failed runs form the
  **dead-letter** list and can be **replayed** from a chosen step. Webhook runs
  can carry an idempotency key so duplicate deliveries reuse the same run.
- **Webhook** — a tokenized URL; `POST` to it triggers the flow. Optionally
  hardened with a per-endpoint **HMAC signing secret** (show-once) — inbound
  requests must send `X-Relay-Timestamp` + `X-Relay-Signature` over
  `{timestamp}.{body}`, checked against a replay window. Every attempt is recorded
  in a **delivery log** classified by outcome (delivered / bad signature / replay
  / duplicate / flow-disabled).
- **Schedule** — a cron-style trigger for a flow. An in-process scheduler (over a
  clock port, fakeable in tests) runs due schedules through the same executor and
  advances each to its next run.

---

## API

Base URL: `http://localhost:5080`. Errors are RFC 7807 `application/problem+json`
(with `traceId` + `instance`). List endpoints are paged (`?page=&pageSize=`,
`pageSize` clamped to 100) and return `{ items, page, pageSize, totalCount, totalPages }`.

**Auth**: every `/api` endpoint (except `POST /api/auth/login` and the public
`POST /api/hooks/{token}`) requires a `Bearer` JWT from login. Requests are
scoped to the caller's workspace — a foreign workspace resource returns **404**,
and actions needing the **Admin** role return **403** for a **Member**. Reads and
running/retrying flows are open to any member; connector/connection/flow/webhook
mutations require Admin.

| Method & path | Purpose |
| --- | --- |
| `GET /health` | Liveness probe (anonymous) |
| `POST /api/auth/login` | Password login → `{ token, expiresAtUtc, user }` |
| `GET /api/auth/me` | Current authenticated user |
| `GET /api/workspaces` · `/{id}` | Workspace directory (scoped to caller) |
| `GET/POST/PUT/DELETE /api/connectors` · `/{id}` | Connector catalog CRUD |
| `GET/POST /api/connectors/{id}/versions` | List / publish connector schema versions |
| `POST /api/connectors/{id}/versions/{v}/deprecate` | Deprecate a version |
| `GET/POST/PUT/DELETE /api/workspaces/{ws}/connections` · `/{id}` | Connection CRUD (config validated against the connector version) |
| `POST /api/workspaces/{ws}/connections/{id}/rotate-secret` | Re-encrypt the stored secret under a new data key |
| `GET/POST/PUT/DELETE /api/workspaces/{ws}/flows` · `/{id}` | Flow CRUD |
| `POST /api/workspaces/{ws}/flows/{id}/enable` · `/disable` | Toggle a flow |
| `POST /api/workspaces/{ws}/flows/{id}/run` | Trigger a flow manually |
| `GET /api/workspaces/{ws}/runs` · `/{runId}` | Run history + detail (`?status=` filter) |
| `GET /api/workspaces/{ws}/dead-letter` | Failed runs (dead-letter list) |
| `GET /api/workspaces/{ws}/metrics` · `/flows/{id}/metrics` | Run metrics (success rate, p50/p95, runs over time) |
| `POST /api/workspaces/{ws}/runs/{runId}/retry` | Re-run with the original payload |
| `POST /api/workspaces/{ws}/runs/{runId}/replay` | Replay, skipping steps before `fromStepOrder` |
| `GET/POST/PUT/DELETE /api/workspaces/{ws}/flows/{id}/schedules` · `/{sid}` | Cron schedules |
| `POST /api/workspaces/{ws}/flows/{id}/schedules/{sid}/enable` · `/disable` | Toggle a schedule |
| `GET /api/workspaces/{ws}/flows/{id}/schedules/preview?cron=` | Validate + preview next runs |
| `GET/POST/DELETE /api/workspaces/{ws}/flows/{id}/webhooks` | Manage webhooks |
| `POST/DELETE /api/workspaces/{ws}/flows/{id}/webhooks/{wid}/signing-secret` | Rotate (show-once) / disable HMAC signing |
| `GET /api/workspaces/{ws}/flows/{id}/webhooks/{wid}/deliveries` | Delivery log (classified) |
| `POST /api/hooks/{token}` | Public inbound webhook trigger (`Idempotency-Key` de-dupes; HMAC-signed when enabled) |

---

## Getting started

Prerequisites: **.NET SDK 10.0.302+**, **Node 20+**, **pnpm 9+**.

### Server

```bash
cd server
dotnet restore
dotnet build
dotnet test                       # xUnit + WebApplicationFactory integration tests
dotnet run --project Relay.Api    # serves the API on http://localhost:5080
```

On first run the API applies EF Core migrations and seeds a demo workspace
(**Acme**, slug `acme`), a user (`owner@acme.test` / `password123`), the connector
catalog, sample connections, a flow, and a webhook (`demo-signup-hook`).

Try a webhook trigger once the server is running:

```bash
curl -X POST http://localhost:5080/api/hooks/demo-signup-hook \
     -H 'content-type: application/json' -d '{"email":"new@user.test"}'
```

### Client

```bash
cd client
pnpm install
pnpm dev             # Vite dev server (http://localhost:5173)
pnpm test            # Vitest + React Testing Library (no server needed)
pnpm build           # tsc + vite build
```

The client reads the API base URL from `VITE_API_BASE_URL` (defaults to
`http://localhost:5080`). See `client/.env.example`. Client tests mock the API
layer, so **no running server is required**.

---

## Architecture

**Server** (clean-ish layering):

- `Relay.Domain` — entities, enums, and execution **ports** (`IActionDispatcher`,
  `IFlowExecutor`). No infrastructure dependencies.
- `Relay.Infrastructure` — EF Core `RelayDbContext`, migrations, seeder, the
  `FlowExecutor`, and the `SimulatedActionDispatcher` adapter.
- `Relay.Api` — controllers, request/response contracts (records with validation),
  shared pagination, ProblemDetails, CORS.
- `Relay.Tests` — xUnit: executor unit tests over an in-memory SQLite DB, and API
  integration tests over `WebApplicationFactory` (Testing environment, in-memory DB).

**Client**: a typed `fetch` wrapper (`ApiError`, `ProblemDetails`), per-feature
api modules, a `useAsync` data hook, a `WorkspaceProvider` context, and pages for
connectors, connections, flows (list + editor), and runs.

### Notes / gotchas respected

- **SQLite + DateTimeOffset**: SQLite can't order/compare `DateTimeOffset`, so a
  value converter stores them as UTC ticks (`long`); enums persist as strings.
- **Record-DTO validation**: attributes target the **constructor parameters**
  (not `[property:]`), and value-type request fields are nullable so a missing
  value is a `400`, not a bind to default.
- **EF**: `Include` is applied before `Skip`/`Take`; replacing a flow's ordered
  steps deletes then re-inserts inside a transaction to respect the unique
  `(FlowId, Order)` index.
- **No external calls**: flow execution is simulated in-process; set a step's
  config to `{"fail": true}` to exercise the retry / failed-run path.

---

## Test coverage

- **Server**: 138 xUnit tests — persistence + DateTimeOffset ordering, connector /
  connection / workspace / flow / run / webhook APIs (incl. 400/404/409 paths),
  executor unit tests (retry, skip-after-failure, transient recovery), pagination,
  validation, ProblemDetails shape, the auth denial matrix (401 / 403 / 404, role
  gating, foreign-workspace isolation), connector versioning + JSON-schema config
  validation, scheduling (cron parsing/next-run, dispatcher over a fake clock,
  schedule API + preview), retries/dead-letter (per-step attempts + backoff over a
  fake delayer, replay-from-step, dead-letter list, webhook idempotency), and
  secret protection (envelope round-trip over a fake KMS, rotation, write-only +
  encrypted-at-rest via the API), webhook hardening (HMAC compute/verify,
  signed/missing/invalid/expired deliveries + classified delivery log), and
  observability (metrics calculator: success rate, nearest-rank p50/p95,
  zero-filled time series; workspace + per-flow metrics API).
- **Client**: 43 Vitest tests — the API wrapper, health/connectors/connections/
  flows/runs pages, the pagination component, the login page and route guard, the
  schema-driven connection form, the cron schedule editor, the dead-letter view,
  secret rotation, webhook signing-secret management + delivery log, and the
  metrics dashboard (tiles, per-flow table, runs-over-time bars), all with the
  API layer mocked.
