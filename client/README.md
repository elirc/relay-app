# relay-app client

The React 19 + TypeScript + Vite single-page app for relay-app. It talks to the .NET API in `../server` over JSON + JWT; the server is the source of truth for everything (see `../docs/architecture.md`).

## Run

```bash
npm install
npm run dev          # Vite dev server
npm test             # vitest run — 53 tests, API layer mocked, no server needed
npx tsc -b           # typecheck
npm run lint         # oxlint
npm run build        # tsc -b && vite build
```

The API base URL is `VITE_API_BASE_URL`, defaulting to `http://localhost:5080` (`src/api/client.ts:4-8`). Start the server first (`../docs/getting-started.md`) and log in with a seeded user.

## Layout

| Path | Role |
| --- | --- |
| `src/App.tsx` | Routes: `/login`, then behind `RequireAuth` — `dashboard`, `connectors`, `connections`, `templates`, `flows`, `flows/new`, `flows/:id`, `runs`, `dead-letter`, `health` (`:19-33`) |
| `src/api/client.ts` | Typed `fetch` wrapper: attaches the bearer token (`setAuthToken`, `:15`), calls a handler on 401 so the app can log out |
| `src/api/*.ts` | One module per server resource (`flows`, `connections`, `webhooks`, `schedules`, `runs`, `metrics`, …), types in `types.ts` |
| `src/auth/AuthContext.tsx` | Token + user persisted in `localStorage` under `relay.token` (`:7`, read at `:23-31`); user under `relay.user` (`:8`); token pushed into the API client during render (`:40`) |
| `src/auth/RequireAuth.tsx` | Redirects unauthenticated users to `/login` |
| `src/workspace/WorkspaceContext.tsx` | Loads the caller's workspaces once authenticated and tracks the current one |
| `src/pages/FlowEditorPage.tsx` | Flow editing; keeps `concurrencyToken` (`:48`, `:60`), sends it as `expectedConcurrencyToken` (`:105`) and handles a 409 (`:115`) |
| `src/components/WebhooksSection.tsx`, `SchedulesSection.tsx` | Per-flow webhook (show-once signing secret) and schedule management |
| `src/lib/schema.ts` | Turns a connector's JSON-Schema subset into form fields and back; mirrors the server's `JsonSchemaValidator` |
| `src/test/setup.ts` | Vitest + jest-dom setup |

## Notes for reviewers

- The JWT lives in `localStorage`, so any XSS in this origin can read it. That is a deliberate simplicity trade-off for a learning app; an httpOnly cookie would remove it.
- Client tests mock the API modules; they prove rendering and state handling, not the contract with the server. The server's `WebApplicationFactory` tests are the contract tests.
- See `../docs/testing.md` for the Vitest load policy (one worker, files sequential).
