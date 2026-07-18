# Getting started

Run both halves locally and walk a flow from login to metrics. Everything works
offline — flow execution is simulated, so no external credentials or network are
needed.

Prerequisites: **.NET SDK 10**, **Node 20+**, **pnpm 9+**.

## Run the server

```bash
cd server
dotnet restore
dotnet run --project Relay.Api        # serves http://localhost:5080
```

On first run the API applies EF Core migrations and seeds:

- workspace **Acme** (slug `acme`),
- admin user **`owner@acme.test` / `password123`**,
- the connector catalog (http, slack, email, sheets, delay) each with a v1 schema,
- demo connections ("Inbound webhook source", "Acme #alerts"),
- a demo flow ("Notify Slack on new signup"),
- and a webhook with token **`demo-signup-hook`**.

Check readiness:

```bash
curl -s http://localhost:5080/health
# {"status":"ok","service":"relay-api","version":"...","checks":{"database":"ok"},"timestampUtc":"..."}
```

## Run the client

```bash
cd client
pnpm install
pnpm dev               # Vite dev server at http://localhost:5173
```

The client reads the API base URL from `VITE_API_BASE_URL` (defaults to
`http://localhost:5080`). Log in with the seeded admin above. Client tests
(`npx vitest run`) mock the api layer, so they need no running server.

## End-to-end walkthrough (curl)

All values below are real seed data. Responses are trimmed for brevity.

### 1. Log in → capture the JWT

```bash
TOKEN=$(curl -s http://localhost:5080/api/auth/login \
  -H 'content-type: application/json' \
  -d '{"email":"owner@acme.test","password":"password123"}' \
  | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

WS=22222222-0000-0000-0000-000000000001        # Acme workspace id (seeded)
AUTH="authorization: Bearer $TOKEN"
```

### 2. Inspect the seeded connection

```bash
curl -s -H "$AUTH" "http://localhost:5080/api/workspaces/$WS/connections" | python -m json.tool
# items[].hasCredentials shows credential presence — the secret itself is never returned.
```

### 3. Look at the seeded flow

```bash
FLOW=55555555-0000-0000-0000-000000000001       # "Notify Slack on new signup"
curl -s -H "$AUTH" "http://localhost:5080/api/workspaces/$WS/flows/$FLOW" | python -m json.tool
```

### 4. Run the flow manually

```bash
curl -s -H "$AUTH" -H 'content-type: application/json' \
  -X POST "http://localhost:5080/api/workspaces/$WS/flows/$FLOW/run" \
  -d '{"payloadJson":"{\"email\":\"manual@user.test\"}"}' | python -m json.tool
# → 201 with status "Succeeded" and a step-log timeline (step 0 = trigger).
```

### 5. Trigger the flow via its webhook

```bash
curl -s -X POST http://localhost:5080/api/hooks/demo-signup-hook \
  -H 'content-type: application/json' -d '{"email":"new@user.test"}'
# → 202 {"runId":"...","status":"Succeeded","deduplicated":false}
```

Send it again with an `Idempotency-Key` and note the second call de-duplicates to
the same run:

```bash
curl -s -X POST http://localhost:5080/api/hooks/demo-signup-hook \
  -H 'content-type: application/json' -H 'Idempotency-Key: demo-1' -d '{"email":"a@b.c"}'
curl -s -X POST http://localhost:5080/api/hooks/demo-signup-hook \
  -H 'content-type: application/json' -H 'Idempotency-Key: demo-1' -d '{"email":"a@b.c"}'
# second → "deduplicated":true with the same runId
```

### 6. Produce and replay a dead-letter run

Create an enabled flow whose only step fails (config `{"fail":true}`), run it, then
replay it from the dead-letter list.

```bash
# Create a failing flow (Admin).
FAIL=$(curl -s -H "$AUTH" -H 'content-type: application/json' \
  -X POST "http://localhost:5080/api/workspaces/$WS/flows" -d '{
    "name":"Failing demo","triggerConnectionId":"44444444-0000-0000-0000-000000000001",
    "steps":[{"name":"Boom","connectionId":"44444444-0000-0000-0000-000000000002",
              "action":"send_message","configJson":"{\"fail\":true}","maxAttempts":1}]
  }' | python -c 'import sys,json;print(json.load(sys.stdin)["id"])')
curl -s -H "$AUTH" -X POST "http://localhost:5080/api/workspaces/$WS/flows/$FAIL/enable" >/dev/null

# Run it → it fails and lands in the dead-letter list.
curl -s -H "$AUTH" -H 'content-type: application/json' \
  -X POST "http://localhost:5080/api/workspaces/$WS/flows/$FAIL/run" -d '{}' >/dev/null
curl -s -H "$AUTH" "http://localhost:5080/api/workspaces/$WS/dead-letter" | python -m json.tool

# Replay the failed run from step 1 (after fixing the cause) → a new run.
RUN=$(curl -s -H "$AUTH" "http://localhost:5080/api/workspaces/$WS/dead-letter" \
  | python -c 'import sys,json;print(json.load(sys.stdin)["items"][0]["id"])')
curl -s -H "$AUTH" -H 'content-type: application/json' \
  -X POST "http://localhost:5080/api/workspaces/$WS/runs/$RUN/replay" -d '{"fromStepOrder":1}' | python -m json.tool
```

### 7. Read the metrics

```bash
curl -s -H "$AUTH" "http://localhost:5080/api/workspaces/$WS/metrics?days=7" | python -m json.tool
# overall summary (successRate, p50/p95), a per-flow breakdown, and a day-by-day series.
```

## Notes

- Every write action above requires the **Admin** role; reads and running/replaying
  a flow are open to any workspace member.
- Trigger endpoints (manual run + inbound webhook) are rate-limited; hammering them
  returns **429**.
- The server writes a local `relay.db` SQLite file on first run. Delete it to reset
  to a clean seeded state.
