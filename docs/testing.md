# Testing

The suite is **229 server** (xUnit) + **53 client** (Vitest 4 + React Testing
Library) tests. Everything runs offline: flow execution goes through a simulated
port, the database is in-memory SQLite, and the client mocks the api layer.

## How to run

```bash
# Server (from /server)
dotnet test                                   # all 229
dotnet test --filter "FullyQualifiedName~MigrationDriftTests"   # one class

# Client (from /client)
npx vitest run                                # all 53, one worker, files sequential
npx vitest run src/pages/FlowEditorPage.test.tsx   # a single file (see load policy)
npx tsc -b                                    # typecheck
npm run lint                                  # oxlint
```

## Server test taxonomy

| Kind | What it covers | Example files |
| --- | --- | --- |
| **Domain / unit** | Pure logic with no DB or HTTP | `CronExpressionTests`, `MetricsCalculatorTests`, `WebhookSignatureTests`, `SecretProtectorTests`, `JsonSchemaValidatorTests` |
| **Executor** | Runs over in-memory SQLite with a fake dispatcher | `FlowExecutorTests`, `RetriesExecutorTests`, `ExecutorExpansionTests`, `ScheduleDispatcherTests`, `SchedulingExpansionTests` |
| **Persistence** | Migrations, seeder, converters, indexes | `PersistenceTests`, `DateTimeOffsetOrderingTests`, `MigrationDriftTests` |
| **API integration** | Full HTTP pipeline over `WebApplicationFactory` | `FlowsApiTests`, `RunsApiTests`, `SecretsApiTests`, `WebhookHardeningApiTests`, `WebhookSecurityBoundaryTests`, `AuthApiTests`, `AuthorizationMatrixTests`, `ConcurrencyExpansionTests`, `ImportExportExpansionTests`, … |

## Harnesses

- **`RelayApiFactory`** (`WebApplicationFactory<Program>`) — boots the real API in
  the **`Testing`** environment (so the production startup migrate/seed is skipped)
  and swaps the file database for a private in-memory SQLite kept alive by an open
  connection. `SeedAsync` applies migrations + seeds; `WithDbAsync` runs against a
  scoped `RelayDbContext`; `AuthenticateAsync`/`LoginAsync` attach a bearer token
  (default: the seeded Admin `owner@acme.test`). Override services per test with
  `WithWebHostBuilder` (e.g. swap `IClock`, lower the rate-limit).
- **`SqliteTestDatabase`** — an isolated in-memory SQLite whose schema is built by
  applying the **real migrations** (`Migrate()` in its ctor). `CreateContext()`
  hands out fresh contexts to defeat the identity map when asserting round trips.
- **`FakeClock`** (`IClock`) — a settable/advanceable clock for deterministic
  scheduling, webhook-freshness, and metrics-window tests.
- **`FakeKms`** (`IKeyManagementService`) — wraps data keys by XOR against a fixed
  mask; exercises the envelope round trip + rotation with no crypto dependency.
- **`FakeActionDispatcher`** (`IActionDispatcher`) — a per-request `Handler` and a
  `Calls` counter, so a test asserts exactly which steps dispatched.
- **`FakeDelayer`** (`IDelayer`) — records requested backoff delays without
  sleeping, so retry/backoff sequencing is asserted instantly.

## Migration-drift guard

`MigrationDriftTests` protects against the EF model, the checked-in migrations, and
the seeder drifting apart:

- `Model_HasNoPendingChanges_NotCapturedByAMigration` fails if
  `Database.HasPendingModelChanges()` is true — i.e. an entity changed without a new
  migration.
- `AllMigrations_AreApplied_AndNonePending` asserts every migration applied and none
  pending.
- `Seeder_RunsCleanly_OnAFreshlyMigratedDatabase` mirrors production startup
  (Migrate → seed) and checks the seeded catalog + demo admin.

The guard was falsified once (adding an unmapped property made all three fail —
EF Core 10's `Migrate()` also rejects pending model changes) and then the probe was
removed, confirming it detects drift rather than passing vacuously. If these tests
go red, run `dotnet ef migrations add <Name>` in `Relay.Infrastructure`.

## Client test policy (Vitest 4 under load)

The client runs on a shared machine. Vitest 4's forks pool can time out **spawning
workers** under load, producing spurious "Errors N" that are **not** test failures.
The config already pins `maxWorkers: 1` and `fileParallelism: false` (the v4-removed
`poolOptions`/`singleFork` knobs are intentionally absent — don't reintroduce them),
with generous 30s timeouts.

For a deterministic green under load, **run one file per invocation**
(`npx vitest run <path>`) and treat a transient worker-spawn error as
retry-in-isolation, not a failure. A full `npx vitest run` passes when the machine
is quiet.

Client tests mock the api modules (`vi.spyOn(api, 'fn')`) and render with a mocked
`WorkspaceContext`, so **no server is required**.
