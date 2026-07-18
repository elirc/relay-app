# 0010 — Remove the Microsoft.OpenApi package

**Status:** Accepted

## Context

The API shipped with the `Microsoft.OpenApi` package for schema generation. On the
.NET 10 toolchain the 2.x line introduced breaking/behavioral churn that added build
friction and a runtime dependency whose value — a generated Swagger document — was
low for a project whose surface is small, stable, and already described by strongly
typed record contracts.

## Decision

Remove the `Microsoft.OpenApi` package. Document the API surface **by hand** in
[`api-reference.md`](../api-reference.md) and keep the request/response contracts as
validated C# records (the single source of truth). Do **not** reintroduce
`Microsoft.OpenApi` 2.x.

## Consequences

- One fewer dependency and no version-churn risk in the build.
- The API reference is authored deliberately — accurate and readable, but it must be
  kept in sync with the contracts by hand (the contracts remain authoritative).
- No live Swagger UI; consumers read the reference doc. For a surface this size that
  is an acceptable trade.
- If a generated spec is wanted later, a different, stable generator can be chosen —
  the constraint is specifically against `Microsoft.OpenApi` 2.x.
