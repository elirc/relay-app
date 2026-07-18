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
(`{ v, wrappedKey, nonce, tag, cipher }`). `EnvelopeSecretProtector` implements
`Protect`/`Reveal`/`Rotate` against the port and zeroes plaintext key material after
use.

- App KMS: `LocalKeyManagementService` — AES-GCM wrapping under a master key
  derived (SHA-256) from `Secrets:MasterKey`. It only ever sees data keys.
- Test KMS: `FakeKms` — wraps by XOR against a fixed mask; no crypto dependency,
  still exercises the full envelope round trip and rotation.

## Consequences

- Rotating the master key (or the KMS) re-wraps data keys without re-encrypting
  every payload; per-secret data keys limit blast radius.
- Tests cover the envelope and rotation with zero external services.
- The master key derivation means any dev string works locally; production must
  supply a real 16/24/32-byte `Secrets:MasterKey`.
- The KMS never sees plaintext secrets — only data keys — keeping the trust boundary
  narrow.
