# Deferred work (check when implementing)

Early API slice only. Items below are **intentional later** — not current defects. When you build each one, re-check security, ops cost, and tests.

## Media storage

| Item | Notes |
|------|--------|
| **~~Postgres `bytea` for photos/videos~~** | Done 2026-09-23: media moved to Cloudflare R2; `MemberMedia` holds keys only. |
| **Purge files of deleted media** | Soft deletes (single photo, video, account deletion) leave the file in R2 — account deletion runs in a DB transaction the bucket cannot join. A reused slot overwrites its file; everything else needs a purge job. |

## Soft-deleted / account-deleted media

| Item | Notes |
|------|--------|
| **Space after soft-delete** | See the purge job above — the bytes now sit in R2, not Postgres. |
| **Background job (planned)** | When an **account is deleted**, run a job that **compresses** related media and stores it in a **secure place** (cold/archive). Then purge hot DB blobs. Re-check: encryption at rest, retention policy, restore/legal hold, job failure retries. |

## Related later items

- Paid/production SMS (beyond Textbelt free quota) and hardened SMTP (dedicated ESP).
- Real face/speech providers, staff APIs, AuditLog retention, CI — see engineering docs.
