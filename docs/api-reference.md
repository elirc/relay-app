# API reference

Base URL: `http://localhost:5080`. All payloads are JSON (`camelCase`; enums are
strings). Errors are RFC 7807 `application/problem+json` with `traceId` and
`instance` extensions.

## Conventions

- **Auth** — every `/api/*` endpoint requires a `Bearer` JWT from
  `POST /api/auth/login`, **except** `POST /api/auth/login` itself, the public
  `POST /api/hooks/{token}`, and `GET /health`. A missing/invalid token → **401**.
- **Tenancy** — requests are scoped to the caller's workspace. A route
  `workspaceId` that isn't the caller's reads as **404** (a foreign resource looks
  absent, not forbidden) — this is checked **before** role.
- **Roles** — `Admin` and `Member`. Reads, running/retrying/replaying flows, and
  schedule/webhook previews are open to any member. Connector, connection, flow,
  schedule, and webhook **mutations require Admin**; an insufficient role → **403**.
- **Pagination** — list endpoints marked _paged_ accept `?page=` (default 1) and
  `?pageSize=` (default 20, clamped to **100**) and return
  `{ items, page, pageSize, totalCount, totalPages }`.
- **Rate limiting** — trigger endpoints (manual run + inbound webhook) share a
  fixed-window limiter; over the limit → **429**.
- **Concurrency** — `PUT` on a flow accepts `expectedConcurrencyToken`; a stale
  token → **409**.

---

## Health

### `GET /health` — anonymous
Liveness + readiness (includes a DB probe).
- **200** `HealthResponse` when the DB is reachable; **503** (same body, `status:
  "degraded"`, `checks.database: "error"`) when it isn't.
- Body: `{ status, service, version, checks: { database }, timestampUtc }`.

---

## Auth

### `POST /api/auth/login` — anonymous
- Body: `{ email, password }` (both required; `email` must be a valid address).
- **200** `{ token, expiresAtUtc, user: AuthUserDto }` · **400** validation ·
  **401** bad credentials (constant reply — never reveals which field was wrong).

### `GET /api/auth/me`
- **200** `AuthUserDto` · **401**.

`AuthUserDto`: `{ userId, email, displayName, role, workspaceId, workspaceName,
workspaceSlug }`.

---

## Workspaces

### `GET /api/workspaces`
Directory scoped to the caller — returns only the caller's workspace.
- **200** `WorkspaceDto[]`.

### `GET /api/workspaces/{id}`
- **200** `WorkspaceDto` · **404** (incl. any workspace that isn't the caller's).

`WorkspaceDto`: `{ id, name, slug, createdAtUtc }`.

---

## Connectors (global catalog)

Reads open to any member; mutations require **Admin**.

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET /api/connectors` _(paged)_ | 200 `Paged<ConnectorDto>` | — |
| `GET /api/connectors/{id}` | 200 `ConnectorDto` | 404 |
| `POST /api/connectors` | 201 `ConnectorDto` | 400, 403, 409 (key exists) |
| `PUT /api/connectors/{id}` | 200 `ConnectorDto` | 400, 403, 404 |
| `DELETE /api/connectors/{id}` | 204 | 403, 404, 409 (in use) |

- **Create** body: `{ key, name, description, authKind, configSchemaJson }`. `key`
  must match `^[a-z0-9-]+$`; a new connector ships an initial **v1** schema version.
- **Update** body: `{ name, description, authKind, configSchemaJson }`. Changing
  the schema **publishes a new version** (prior versions remain for connections
  already on them).

`ConnectorDto`: `{ id, key, name, description, authKind, configSchemaJson,
latestVersion, isLatestDeprecated, createdAtUtc }`.

### Connector versions

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET /api/connectors/{id}/versions` | 200 `ConnectorVersionDto[]` | 404 |
| `POST /api/connectors/{id}/versions` | 201 `ConnectorVersionDto` | 400, 403, 404 |
| `POST /api/connectors/{id}/versions/{v}/deprecate` | 200 `ConnectorVersionDto` | 403, 404 |

- **Publish** body: `{ configSchemaJson }`; the new version number is
  `max(existing) + 1`. Deprecating a version steers new installs to a newer one.

`ConnectorVersionDto`: `{ id, connectorId, version, configSchemaJson, isDeprecated,
createdAtUtc }`.

---

## Connections (per workspace)

Route prefix `/api/workspaces/{ws}/connections`. Reads open to any member;
mutations require **Admin**.

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET .../connections` _(paged)_ | 200 `Paged<ConnectionDto>` | 404 (workspace) |
| `GET .../connections/{id}` | 200 `ConnectionDto` | 404 |
| `POST .../connections` | 201 `ConnectionDto` | 400, 403, 404 |
| `PUT .../connections/{id}` | 200 `ConnectionDto` | 400, 403, 404 |
| `POST .../connections/{id}/rotate-secret` | 200 `ConnectionDto` | 400 (no secret), 403, 404 |
| `DELETE .../connections/{id}` | 204 | 403, 404, 409 (used by a flow) |

- **Create** body: `{ connectorId, name, configJson, credentialsJson,
  connectorVersion? }`. `configJson` is validated against the target connector
  **version's** JSON schema (defaults to the latest non-deprecated version;
  installing on a deprecated version → **400**). `credentialsJson` is encrypted at
  rest and **never** returned.
- **Update** body: `{ name, configJson, credentialsJson, status }`. `configJson`
  is validated against the connection's own pinned schema version. `credentialsJson`
  is **write-only**: `null` leaves the secret untouched, `"{}"`/empty clears it, a
  value re-seals it.
- **Rotate-secret** re-encrypts the stored secret under a fresh data key (ciphertext
  changes, value preserved).

`ConnectionDto`: `{ id, workspaceId, connectorId, connectorKey, connectorName,
connectorVersion, isVersionDeprecated, name, configJson, hasCredentials, status,
createdAtUtc, updatedAtUtc }` — note **no secret**, only `hasCredentials`.

---

## Flows (per workspace)

Route prefix `/api/workspaces/{ws}/flows`. Reads open to any member; mutations
require **Admin**.

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET .../flows` _(paged)_ | 200 `Paged<FlowSummaryDto>` | 404 |
| `GET .../flows/{id}` | 200 `FlowDetailDto` | 404 |
| `POST .../flows` | 201 `FlowDetailDto` | 400, 403, 404 |
| `PUT .../flows/{id}` | 200 `FlowDetailDto` | 400, 403, 404, **409** (stale token) |
| `POST .../flows/{id}/enable` | 200 `FlowSummaryDto` | 403, 404 |
| `POST .../flows/{id}/disable` | 200 `FlowSummaryDto` | 403, 404 |
| `DELETE .../flows/{id}` | 204 | 403, 404 |
| `POST .../flows/from-template/{templateId}` | 201 `FlowDetailDto` | 400, 403, 404 |
| `GET .../flows/{id}/export` | 200 `FlowExportDto` | 404 |
| `POST .../flows/import?dryRun=` | 200 `ImportResultDto` | 400, 403, 404 |

- **Create/Update** body: `{ name, description?, triggerConnectionId, steps[],
  expectedConcurrencyToken? }` where each step is `{ name, connectionId, action,
  configJson, maxAttempts?, backoffSeconds? }`. At least one step is required; the
  trigger and every step connection must live in the workspace (else **400**). New
  flows are created **disabled**. `maxAttempts` clamps to 1–10, `backoffSeconds` to
  0–3600. Update replaces the whole step list.
- **From-template** maps the template's connector keys to the workspace's
  connections; an unmapped connector → **400**. Produces a disabled draft.
- **Import** (`dryRun=true` validates only) is idempotent by `externalId`
  (create vs update); a missing name/steps or unresolvable connector → invalid
  (dry-run reports it; a real import → **400**).

`FlowDetailDto`: `{ id, workspaceId, name, description, isEnabled,
triggerConnectionId, triggerConnectionName, steps[], concurrencyToken,
createdAtUtc, updatedAtUtc }`.
`ImportResultDto`: `{ valid, action ("create"|"update"|"invalid"), flowId, issues[] }`.

### Flow templates (global)

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET /api/flow-templates` | 200 `FlowTemplateDto[]` | — |
| `GET /api/flow-templates/{id}` | 200 `FlowTemplateDto` | 404 |

---

## Runs (per workspace)

Route prefix `/api/workspaces/{ws}`. Running/retrying/replaying is open to any
member.

| Method & path | Success | Errors |
| --- | --- | --- |
| `POST .../flows/{flowId}/run` | 201 `RunDetailDto` | 404, **429** |
| `GET .../runs?status=&page=&pageSize=` _(paged)_ | 200 `Paged<RunSummaryDto>` | 404 |
| `GET .../dead-letter` _(paged)_ | 200 `Paged<RunSummaryDto>` (failed only) | 404 |
| `GET .../runs/{runId}` | 200 `RunDetailDto` | 404 |
| `POST .../runs/{runId}/retry` | 201 `RunDetailDto` | 404 |
| `POST .../runs/{runId}/replay` | 201 `RunDetailDto` | 404 |

- **Run** body (optional): `{ payloadJson }`. **Retry** re-runs the whole flow with
  the original payload. **Replay** body (optional): `{ fromStepOrder }` (0–1000) —
  action steps before it are logged Skipped, the rest run.

`RunDetailDto`: `{ id, flowId, flowName, status, trigger, error, triggerPayloadJson,
idempotencyKey, startedAtUtc, completedAtUtc, durationMs, retryCount, stepLogs[] }`;
each `stepLog` is `{ id, stepOrder, name, status, message, startedAtUtc,
completedAtUtc, durationMs }` (step 0 = the trigger).

---

## Metrics (per workspace)

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET .../metrics?days=` | 200 `WorkspaceMetricsDto` | 404 |
| `GET .../flows/{flowId}/metrics?days=` | 200 `FlowMetricsDto` | 404 |

`days` clamps to 1–90 (default 7). `MetricsSummary`: `{ totalRuns, succeeded,
failed, successRate, p50DurationMs, p95DurationMs }`. `WorkspaceMetricsDto`:
`{ days, overall: MetricsSummary, perFlow: [{ flowId, flowName, summary }],
runsOverTime: [{ date, total, succeeded, failed }] }`.

---

## Schedules (per flow)

Route prefix `/api/workspaces/{ws}/flows/{flowId}/schedules`. Mutations require
**Admin**.

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET .../schedules` | 200 `ScheduleDto[]` | 404 |
| `GET .../schedules/preview?cron=&count=` | 200 `SchedulePreviewResponse` | — |
| `POST .../schedules` | 201 `ScheduleDto` | 400, 403, 404 |
| `PUT .../schedules/{id}` | 200 `ScheduleDto` | 400, 403, 404 |
| `POST .../schedules/{id}/enable` | 200 `ScheduleDto` | 403, 404 |
| `POST .../schedules/{id}/disable` | 200 `ScheduleDto` | 403, 404 |
| `DELETE .../schedules/{id}` | 204 | 403, 404 |

- **Create/Update** body: `{ cronExpression }` (5-field cron; invalid → **400**).
  `preview` validates a cron and returns the next `count` (1–10, default 5) fire
  times without persisting. Disabling clears `nextRunAtUtc`; enabling re-arms it.

`ScheduleDto`: `{ id, flowId, cronExpression, isEnabled, nextRunAtUtc, lastRunAtUtc,
createdAtUtc, updatedAtUtc }`. `SchedulePreviewResponse`: `{ valid, error, nextRuns[] }`.

---

## Webhooks (per flow)

Route prefix `/api/workspaces/{ws}/flows/{flowId}/webhooks`. Mutations require
**Admin**.

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET .../webhooks` | 200 `WebhookDto[]` | 404 |
| `POST .../webhooks` | 201 `WebhookDto` | 403, 404 |
| `POST .../webhooks/{id}/signing-secret` | 200 `SigningSecretResponse` | 403, 404 |
| `DELETE .../webhooks/{id}/signing-secret` | 204 | 403, 404 |
| `GET .../webhooks/{id}/deliveries` _(paged)_ | 200 `Paged<WebhookDeliveryDto>` | 404 |
| `DELETE .../webhooks/{id}` | 204 | 403, 404 |

- **Create** mints an unguessable token and returns the public URL.
- **Signing-secret** (re-)generates the HMAC secret and turns on verification —
  the plaintext secret is returned **once** and never again. `DELETE` clears it and
  turns verification off.

`WebhookDto`: `{ id, flowId, token, url, isEnabled, requireSignature,
hasSigningSecret, createdAtUtc, lastTriggeredAtUtc }`. `SigningSecretResponse`:
`{ signingSecret, timestampHeader, signatureHeader }`. `WebhookDeliveryDto`:
`{ id, receivedAtUtc, success, outcome, runId, detail }`.

---

## Public inbound webhook

### `POST /api/hooks/{token}` — anonymous, rate-limited
Triggers the flow bound to `{token}` with the request body as the run payload.

- **Headers** (when signing is enabled): `X-Relay-Timestamp` (unix seconds) and
  `X-Relay-Signature` (lowercase-hex HMAC-SHA256 over `{timestamp}.{body}`).
  Optional `Idempotency-Key` de-duplicates deliveries.
- **202** `{ runId, status, deduplicated }` on success (or a de-duplicated replay).
- **401** signature missing/invalid or timestamp outside the ±5-minute window ·
  **404** unknown/disabled token · **409** the flow is disabled · **429** over the
  rate limit. Every attempt is recorded in the delivery log.
