# 0004 — Write-only secrets

**Status:** Accepted

## Context

Even encrypted at rest, a secret that any endpoint can echo back is one logging
mistake or over-broad DTO away from leaking. Clients need to know whether a secret
is _set_, but never its value.

## Decision

Treat stored secrets as **write-only** from the API's perspective. `Reveal` on
`ISecretProtector` exists only for internal use (webhook signature verification,
rotation) — never to return a value to a client. Response DTOs expose a boolean
instead of the secret: `ConnectionDto.hasCredentials`, `WebhookDto.hasSigningSecret`.
A webhook signing secret is the one exception where the plaintext is returned — and
only **once**, at generation time, with the DTO documented as show-once.

## Consequences

- No response body carries a credential; tests sweep every connection endpoint
  (list/get/update/rotate) asserting the marker string never appears.
- Update semantics for credentials are explicit: `null` preserves, `"{}"`/empty
  clears, a value re-seals — so "I didn't send it" never accidentally wipes a secret.
- Losing a webhook signing secret means rotating it (a new show-once value), not
  reading the old one — which is the correct security posture.
