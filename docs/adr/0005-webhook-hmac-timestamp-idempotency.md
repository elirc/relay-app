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
