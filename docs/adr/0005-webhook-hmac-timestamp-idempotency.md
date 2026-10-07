# 0005 — Webhook verification: HMAC + timestamp + idempotency

**Status:** Accepted

## Context

Inbound webhooks are public (no bearer token — the unguessable path token is the
credential). Left unhardened, an attacker who captures a request could tamper with
the body or replay it, and a well-meaning sender that retries could double-fire the
flow.

## Decision

Layer three independent protections on `POST /api/hooks/{token}`:

1. **HMAC signature** over `{timestamp}.{body}` (HMAC-SHA256, lowercase hex,
   verified constant-time with `FixedTimeEquals`). Signing the body defeats body
   tampering; signing the timestamp binds the request to a moment. Missing/malformed
   → 401.
2. **Timestamp window** — absolute drift from the `IClock` must be ≤ 5 minutes
   (tolerating mild forward skew). Outside the window → 401. This is the anti-replay
   bound.
3. **Idempotency key** — an `Idempotency-Key` header, backed by a unique
   `(FlowId, IdempotencyKey)` index plus an explicit lookup, so a duplicate delivery
   reuses the original run (`deduplicated: true`) instead of creating a new one.

Every attempt is written to a delivery log classified by outcome.

## Consequences

- The three concerns are separable: signing can be on with or without a caller
  sending an idempotency key; the timestamp window is meaningful only when signing
  is on.
- Boundary behavior is testable with a fake clock (exactly-at-limit accepted, one
  second past rejected; a signed replay with the same key returns the same run).
- Rotating the signing secret invalidates old signatures immediately.
- The 5-minute window is a fixed policy constant; senders with large clock skew must
  correct their clocks.

## Where it lives in the code

All in `server/Relay.Api/Controllers/HooksController.cs` unless noted:

| Step | Lines |
| --- | --- |
| Token lookup; unknown or disabled webhook → 404 | `:51-56` |
| Body read once (signed payload = run input) | `:59`, `:140-145` |
| Signature block runs only if `RequireSignature` **and** a secret exists | `:62` |
| Missing headers / stale timestamp / bad MAC → 401, each logged with its own outcome | `:67-75`, `:109-114` |
| 5-minute window, absolute drift against `IClock` | `:27`, `:132-138` |
| Disabled flow → 409 | `:78-83` |
| Idempotency lookup → 202 with `deduplicated: true` | `:86-97` |
| Run executed inline, then `Delivered` logged | `:99-106` |
| HMAC over `{timestamp}.{body}`, `FixedTimeEquals` | `server/Relay.Domain/Security/WebhookSignature.cs:14-19`, `:22-38` |
| Unique `(FlowId, IdempotencyKey)` | `server/Relay.Infrastructure/Persistence/RelayDbContext.cs:148` |

## Known gaps

- **Check-then-insert race.** Two concurrent deliveries with the same key can both miss the lookup at `:89-90` and both run the flow; the run row is only saved at the end of execution (`FlowExecutor.cs:166`), so the second save hits the unique index as an unhandled `DbUpdateException` (500) *after* the flow's steps already dispatched twice. No test sends concurrent requests (there is no `Task.WhenAll` anywhere in `server/Relay.Tests`).
- **Replays inside the window without a key.** The timestamp bounds replay to 5 minutes but does not prevent it: a captured signed request without `Idempotency-Key` can be replayed for a new run until the window closes. A nonce store, or requiring the key whenever signing is on, would close this.
- A row with `RequireSignature = true` but a blank secret skips verification entirely (`:62`). The API never produces that state (`WebhooksController.cs:79-81`, `:99-100`), so this is defensive only, but failing closed would be safer.
- The `triggers` rate-limit policy partitions on the constant `"global"` (`server/Relay.Api/Program.cs:73`), so one noisy webhook consumes the budget of every webhook and manual run.
