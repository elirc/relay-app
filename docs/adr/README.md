# Architecture Decision Records

Short records of the load-bearing decisions in relay-app — the context, the
choice, and what it cost. Each captures a decision that is visible in the code and
tests today.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-ports-based-executor.md) | Ports-based flow executor (no real external calls) | Accepted |
| [0002](0002-cron-over-a-clock-port.md) | Cron scheduling over a clock port | Accepted |
| [0003](0003-envelope-encryption-over-a-kms-port.md) | Envelope encryption over a KMS port (+ fake KMS) | Accepted |
| [0004](0004-write-only-secrets.md) | Write-only secrets | Accepted |
| [0005](0005-webhook-hmac-timestamp-idempotency.md) | Webhook verification: HMAC + timestamp + idempotency | Accepted |
| [0006](0006-executedelete-step-replacement.md) | ExecuteDeleteAsync step replacement in a transaction | Accepted |
| [0007](0007-external-id-import-mapping.md) | External-id mapping for idempotent import | Accepted |
| [0008](0008-utc-ticks-datetimeoffset-converter.md) | UTC-ticks converter for DateTimeOffset on SQLite | Accepted |
| [0009](0009-optimistic-concurrency-on-flows.md) | Optimistic concurrency on flows | Accepted |
| [0010](0010-remove-openapi-package.md) | Remove the Microsoft.OpenApi package | Accepted |

Format: **Context → Decision → Consequences**.
