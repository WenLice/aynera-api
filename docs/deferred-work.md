# Deferred work (check when implementing)

Early API slice only. Items below are **intentional later** — not current defects. When you build each one, re-check security, ops cost, and tests.

## Media storage

| Item | Notes |
|------|--------|
| **Postgres `bytea` for photos/videos** | Temporary until dedicated server / object storage is available. Fine for the current few APIs. |
| **Move off DB blobs** | After server purchase: store media on disk or object storage; keep metadata in Postgres. Re-check CDN, signed URLs, backup size. |

## Soft-deleted / account-deleted media

| Item | Notes |
|------|--------|
| **Space after soft-delete** | Soft-deleted rows may still hold bytes in Postgres. Not a problem at current scale. |
| **Background job (planned)** | When an **account is deleted**, run a job that **compresses** related media and stores it in a **secure place** (cold/archive). Then purge hot DB blobs. Re-check: encryption at rest, retention policy, restore/legal hold, job failure retries. |

## Related later items

- Paid/production SMS (beyond Textbelt free quota) and hardened SMTP (dedicated ESP).
- Real face/speech providers, staff APIs, AuditLog retention, CI — see engineering docs.
