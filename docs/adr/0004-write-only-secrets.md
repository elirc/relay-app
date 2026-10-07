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

## Where it lives in the code

- `server/Relay.Api/Controllers/ConnectionsController.cs:30` — `ProtectOrNull`: blank or `"{}"` → `null` (cleared), anything else → sealed envelope. Used on create (`:130`) and on update only when `CredentialsJson` is not null (`:174-176`); rotation re-seals in place (`:207`).
- `server/Relay.Api/Contracts/Connections/ConnectionDto.cs:20` — `HasCredentials` boolean, never the envelope.
- `server/Relay.Api/Controllers/WebhooksController.cs:73-85` — `GenerateSigningSecret`: 32 random bytes as lowercase hex (`:79`), stored sealed (`:80`), `RequireSignature` switched on (`:81`), plaintext returned once (`:85`). Deleting the secret also turns signing off (`:99-100`).
- `server/Relay.Api/Contracts/Webhooks/WebhookDtos.cs:13`, `:19` — `HasSigningSecret`.

## Known gaps

- Generating a new signing secret replaces the old one immediately; there is no overlap window, so a sender cannot rotate without a short period of 401s.
- "Write-only" is enforced by DTO shape and tests, not by the type system: any future endpoint that maps the entity directly (or logs it) would expose the envelope.
