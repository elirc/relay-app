# relay-app

A Zapier-style **integrations / connectors platform**. Workspaces install
**connectors** (integration types) as **connections** (configured instances with
credentials), compose them into **flows** (a trigger + ordered action steps), and
execute those flows as **runs** — manually or via inbound **webhooks** — with full
per-step logging and retry.

This is a monorepo:

| Path      | Stack                                                        |
| --------- | ----------------------------------------------------------- |
| `/server` | ASP.NET Core Web API, .NET 10, EF Core + SQLite, xUnit      |
| `/client` | Vite + React + TypeScript (strict), React Router, Vitest    |

> Scaffold in progress — see the sprint PRs for feature history.

## Getting started

Prerequisites: **.NET SDK 10.0.302+**, **Node 20+**, **pnpm 9+**.

### Server

```bash
cd server
dotnet restore
dotnet build
dotnet test          # xUnit + WebApplicationFactory integration tests
dotnet run --project Relay.Api   # serves the API (Swagger at /swagger in Dev)
```

### Client

```bash
cd client
pnpm install
pnpm dev             # Vite dev server
pnpm test            # Vitest + React Testing Library (no server needed)
pnpm build           # tsc + vite build
```

The client expects the API base URL in `VITE_API_BASE_URL` (defaults to
`http://localhost:5080`). See `client/.env.example`.
