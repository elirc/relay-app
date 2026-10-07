# 0003 — Envelope encryption over a KMS port (+ fake KMS)

**Status:** Accepted

## Context

Connections store credentials and webhooks store HMAC signing secrets. These must
be encrypted at rest, rotatable, and protectable without shipping a real cloud KMS
dependency or requiring one in tests.

## Decision

Use **envelope encryption** over an `IKeyManagementService` port. Each secret is
sealed with AES-GCM under a freshly generated **data key**; the KMS wraps that data
key under a master key, and the wrapped key travels inside the envelope JSON
(`{ "V", "WrappedKey", "Nonce", "Tag", "Cipher" }` — `System.Text.Json` default PascalCase names of the private `Envelope` record). `EnvelopeSecretProtector` implements
`Protect`/`Reveal`/`Rotate` against the port and zeroes plaintext key material after
use.

- App KMS: `LocalKeyManagementService` — AES-GCM wrapping under a master key
  derived (SHA-256) from `Secrets:MasterKey`. It only ever sees data keys.
- Test KMS: `FakeKms` — wraps by XOR against a fixed mask; no crypto dependency,
  still exercises the full envelope round trip and rotation.

## Consequences

- Per-secret data keys limit blast radius. Envelope encryption *permits* rotating
  the master key by re-wrapping data keys only, but no such operation exists in the
  code: `Rotate` (`EnvelopeSecretProtector.cs:70`) is `Protect(Reveal(envelope))`,
  i.e. a full re-encryption under a new data key and the **same** master key.
- Tests cover the envelope and rotation with zero external services.
- The master key is always `SHA-256(Secrets:MasterKey)` (`DependencyInjection.cs:45-46`),
  so any string works and the result is always 32 bytes; the 16/24/32-byte check in
  `LocalKeyManagementService` (`:21-22`) can never fail in the app. Production must
  supply a high-entropy value — and must supply one at all: if the key is missing the
  code silently falls back to the constant `"relay-dev-master-key"`, and
  `appsettings.json` ships `"relay-dev-master-key-change-me"`.
- The KMS never sees plaintext secrets — only data keys — keeping the trust boundary
  narrow.

## Where it lives in the code

- `server/Relay.Infrastructure/Security/EnvelopeSecretProtector.cs` — `Protect` (`:22-43`): fresh data key, 12-byte random nonce, AES-GCM with a 16-byte tag, plaintext data key zeroed at `:34`; `Reveal` (`:45-68`) zeroes the unwrapped key and plaintext buffer in `finally`.
- `server/Relay.Infrastructure/Security/LocalKeyManagementService.cs` — wrapped key layout is `nonce(12) | tag(16) | cipher` (`Wrap`, `:43-56`; `UnwrapDataKey`, `:32-41`).
- Tests: `server/Relay.Tests/SecretProtectorTests.cs`, `SecretsApiTests.cs`, `SecretsExpansionTests.cs`; test KMS `server/Relay.Tests/Support/FakeKms.cs`.

## Known gaps

- No associated data: AES-GCM is called without AAD (`EnvelopeSecretProtector.cs:32`), so an envelope copied from one connection row to another decrypts fine. Binding the row id as AAD would make swaps detectable.
- `Reveal` returns a managed `string` (`:61`); zeroing the byte buffer does not erase the immutable string copy.
- No startup guard rejects the default master key outside Development.
