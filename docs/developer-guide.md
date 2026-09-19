# aynera-api — developer guide

Onboarding guide for the Aynera backend. Use this when you join the project or pick up backend work.

Product specs and feature plans live in **`aynera-admin/docs`** (especially `engineering/`). This repo keeps setup, structure, this guide, and the [API reference](./api-reference.md).

| Doc | Use when |
|-----|----------|
| [setup.md](./setup.md) | First local run |
| [structure.md](./structure.md) | Folder / project map |
| [api-reference.md](./api-reference.md) | Endpoint contracts for clients |
| [deferred-work.md](./deferred-work.md) | Later media/storage jobs — revisit when implementing |
| Architecture (admin docs) | Cross-repo architecture under `aynera-admin/docs/engineering/architecture.md` |
| Auth plan (admin docs) | `aynera-admin/docs/engineering/backend/auth/plan.md` |

---

## What this service is

`aynera-api` is an **ASP.NET Core modular monolith** for Aynera:

- Host intended as `api.aynera.com`
- Stack: **.NET 10**, **PostgreSQL**, **Redis**
- First live slice: **member phone/email OTP or password** for the member app (`aud=member`)
- Clients: `aynera-web`, `aynera-admin`, `aynera-app`

It is **not** a microservices fleet. Add features as folders under Application / Api, not as new deployables, unless an ADR says otherwise (`aynera-admin/docs/engineering/adr/001-modular-monolith.md`).

---

## Solution projects

| Project | Owns |
|---------|------|
| **Aynera.Domain** | Requests/Responses/Records, enums, static helpers, domain exceptions. **No** EF, Identity, or HTTP. |
| **Aynera.Application** | Feature services + **repository interfaces** + config models + notification contracts (`IEmailService`, `ISmsService`). |
| **Aynera.Persistence** | Entities (`AppUser`, `RefreshSession`), `AyneraDbContext`, EF migrations. |
| **Aynera.Infrastructure** | Repository implementations, Redis OTP, JWT signer, DI wiring for infra. |
| **Aynera.Notifications** | Email/SMS adapters (Console, SMTP, Textbelt) implementing Application contracts. |
| **Aynera.Api** | Controllers, middleware, Swagger, host `Program.cs`. |

### Allowed references (one-way)

```text
Domain  ←  Application
Domain  ←  Persistence
Application + Persistence + Domain  ←  Infrastructure
Application  ←  Notifications
Application + Infrastructure + Notifications + Persistence + Domain  ←  Api
```

Never add a project reference that points “up” (Domain must not know Application; Application must not know Infrastructure).

---

## Call flow (house style)

Member registration is the first coordinated workflow: MembersController → RegistrationService → transaction → Identity/account, Profiles and verification-delivery queue. See [registration workflow](registration-workflow.md) for atomicity, delivery retries and migration details. Registration is no longer part of IAuthService.

Member activation, deactivation and deletion are owned by AccountLifecycleService under Features/Users. AuthService retains login, credentials, identifier verification and session operations, plus the existing account-response composition. Authenticated reactivation remains on `POST /members/me/reactivate`. Expired-token recovery uses a reactivation OTP on `POST /members/reactivate/request` and `POST /members/reactivate/recover`.

Deletion now uses WorkflowTransaction to commit account/profile/media/session soft deletion and pending verification-email cancellation together. It preserves soft-deleted media bytes under the existing retention behavior. The success audit event is attempted after commit.

Deactivation also uses WorkflowTransaction: the inactive flag/timestamp and revocation of all refresh sessions commit together. PostgreSQL tests inject failures after each write and cancellation after revocation, verify rollback and then retry. Audit is attempted after commit. Existing access JWTs are not revoked by this change. After they expire, reactivation recovery is the verified OTP path above; it does not restore revoked refresh sessions.

```text
HTTP → Controller → Service → Repository → Database / Redis
```

| Layer | Do | Don’t |
|-------|----|--------|
| **Controller** | Route, auth policy, bind body/query, map to `ApiResponse<T>` | Business rules, SQL, Redis |
| **Service** | Feature logic, orchestration, throw typed domain errors | Talk to EF `DbContext` directly (use repos) |
| **Repository** | Persistence for **one** model/store concern | HTTP or cross-feature policy |
| **Helpers / statics** | Shared pure utilities used from multiple places | Hide business workflows |

Auth example path:

```text
AuthController
  → AuthService
  → IUserRepository / IRefreshSessionRepository / IOtpChallengeRepository
  → Postgres (Persistence) + Redis
```

---

## Auth feature layout (template for new features)

```text
Aynera.Application/Features/Auth/
├── Models/                 # JwtOptions, OtpOptions, token result records
├── Repositories/           # IUserRepository, IRefreshSessionRepository, IOtpChallengeRepository
└── Services/
    ├── Interfaces/         # IAuthService, ITokenService, ISmsService, IEmailService
    └── Implementations/    # AuthService

Aynera.Infrastructure/
├── Repositories/           # EF / Redis implementations
└── Services/               # JwtTokenService, CurrentUser, …

Aynera.Notifications/
├── Email/                  # ConsoleEmailService, SmtpEmailService
├── Sms/                    # ConsoleSmsService, TextbeltSmsService
└── DependencyInjection.cs  # AddAyneraNotifications

Aynera.Api/Controllers/
├── AdminsController.cs      # admins/*  — admin self + super-admin management (SuperAdmin)
├── AdmissionsController.cs  # admissions/*
├── AuditController.cs       # audit/*
├── AuthController.cs        # auth/*    — member login/tokens + admin login under auth/admin/*
├── EarlyAccessController.cs # early-access/*
├── FeedbackController.cs    # feedback
├── HealthController.cs      # health
├── MembersController.cs     # members/* — register, me/* account+lifecycle, recovery, staff {id}/* (tag "Members · Staff")
├── IntroductionVideoController.cs # introduction-video/* — member video (me*), staff {id}/content (tag "IntroductionVideo · Staff")
├── PhotosController.cs      # photos/* — member photos (Upload, GetAll, {photoId}); staff {id}/{photoId} (tag "Photos · Staff")
├── SuggestionsController.cs # suggestions
└── VenuesController.cs      # venues/*

Route convention (enforced by ControllerRoutingTests): list actions end in `/GetAll`, collection POSTs carry
an action segment (`/Create`, `/Upload`, `/register`), a bare controller prefix is never a route, and no
path is served by both GET and POST. Single-resource routes stay REST: `GET|PATCH|DELETE …/{id}`, `…/me`.

Aynera.Domain/Auth/
├── Requests/               # inbound request DTOs (one type per file)
├── Responses/              # API/response DTOs
├── Records/                # application-facing projections
├── Validators/             # FluentValidation for request DTOs
├── Statics/ / Exceptions/
```

**Convention:** keep service **interfaces** and **implementations** in separate folders. Keep **repository interfaces** in Application; implementations in Infrastructure.

**Mapping (`IMapper` / AutoMapper):**
- **Infrastructure** profiles: Persistence entity ↔ Domain Record
- **Application** profiles: Domain Record → Response DTO
- Inject `IMapper`; do not put AutoMapper in Domain
- Keep Request → Record construction and Identity role enrichment explicit (orchestration, not projection)
- Register once from Infrastructure DI (`AddAutoMapper` for Application + Infrastructure assemblies)

**Audit:**
- `AuditLogs` — developer/ops lines (RequestLogging, shared header)
- `AuditEvents` — admin actions with optional `Changes` JSON; FK `AuditLogId`
- Inject `IAuditWriter` / `IAuditLogWriter`; never log OTP/tokens/media bytes; mask phones

**Diagnostic logging (`ILogger<T>` → Serilog):**
- Controllers: global `DiagnosticLoggingFilter` logs action entry (no per-action logger required)
- Services: Information for start/success; Warning for expected failures (`AuthException`, rate limits)
- Repositories: Debug for mutating entry; Error when infrastructure update fails
- Warning+ also persists to `AuditLogs` via `AuditLogSerilogSink` (Console + optional Seq always)
- Tests: pass a no-op `ILogger<T>` (`DiscardLogger<T>.Instance`)

When adding a feature (for example Profiles):

1. Domain Requests / Responses / Records (one type per file), exceptions, and FluentValidation validators under `Domain/<Feature>/`
2. Application `Features/<Feature>/` with Models, Repositories, Services/Interfaces|Implementations
3. Persistence entities + migration if needed
4. Infrastructure repository (and adapters)
5. Api controller inheriting `BaseController`
6. Register DI in Application / Infrastructure / Notifications; register Domain validators from Api (`AddValidatorsFromAssemblyContaining`)
7. Update [api-reference.md](./api-reference.md)

---

## HTTP and responses

- Controllers inherit **`BaseController`** (`IControllerBase`).
- Success and many failures use **`ApiResponse<T>`**:

```json
{
  "success": true,
  "data": { },
  "statusCode": 200,
  "errorCode": null,
  "errors": null,
  "correlationId": "…"
}
```

- Validation failures: FluentValidation → `400` + `errorCode: "validation_failed"` and field `errors`.
- Auth domain failures: `AuthException` → middleware writes `ApiResponse` with the exception’s status / code.
- Clients should send / receive **`X-Correlation-Id`** (middleware generates one if missing).

Full endpoint list: [api-reference.md](./api-reference.md).  
Interactive docs (Development): Swagger UI at `/swagger`, serving three documents (dropdown, top right). **Aynera API (all endpoints)** is the default and holds everything, so staff endpoints are visible without switching definitions; **Member API** and **Admin API** are the per-audience views. Audience is derived from each action's authorization policy in `Aynera.Api/OpenApi/ApiAudience.cs` (`Of`); anonymous admin-login actions opt into the Admin doc with `[ApiExplorerSettings(GroupName = ApiAudience.Admin)]`. `ApiAudience.IncludedIn` is the document-inclusion rule and is guarded by `ControllerRoutingTests`. Visibility is not access: every document shows the same `Bearer` requirement, and an admin endpoint listed in the combined document still refuses a member token.

Routing convention: every route begins with its controller's `[Route]` prefix. There are no `admin/*` or `public/*` URL prefixes — authorization is entirely by policy, and `ControllerRoutingTests` enforces both rules.

---

## Configuration

Bound mainly under `Aynera:*` in `appsettings.json` / environment:

| Area | Keys / notes |
|------|----------------|
| Postgres | `ConnectionStrings:Aynera` or `AYNERA_DB_CONNECTION` |
| Redis | `Aynera:Redis` / `AYNERA_REDIS` |
| JWT | `Aynera:Jwt` — signing key ≥ 32 chars; audiences `member`, `admin`; access 1 hour; member refresh 90 days; admin refresh 24 hours |
| OTP | `Aynera:Otp` — length, TTL, attempt and rate caps |
| Email | `Aynera:Email` — `Provider` Console\|Smtp; SMTP host/from; verify link base URL |
| SMS | `Aynera:Sms` — `Provider` Console\|Textbelt; Textbelt key (free `textbelt`) |
| CORS | `Aynera:Cors:Origins` — local web/admin ports by default |
| Photos | `Aynera:Photos` — max count/bytes, stub face-match status |
| Introduction video | `Aynera:IntroductionVideo` — max bytes, banned words, stub transcript |
| Media authenticity | `Aynera:MediaAuthenticity` — AI-tool markers; reject synthetic photo/video |
| Early access | `Aynera:EarlyAccess` — IP rate limit (cities are DB-backed) |
| Feedback | `Aynera:Feedback` — grievance max length / IP rate limit |
| Suggestions | `Aynera:Suggestions` — idea max length / IP rate limit |

See `.env.example` and [setup.md](./setup.md).

**Media storage:** photos/videos use Postgres `bytea` temporarily (until dedicated server / object storage). Soft-delete space and account-delete archive job are deferred — see [deferred-work.md](./deferred-work.md).

**Email / SMS:** live in **Aynera.Notifications** behind `IEmailService` / `ISmsService`. `Provider` = `Console` (default), `Smtp`, or `Textbelt`. Never logs OTP, phones, emails, or verify URLs. Api registers `AddAyneraNotifications`. Integration tests replace with capturing doubles.

---

## Database and migrations

- Schema is **EF Core code-first**. Do not hand-create app tables in Postgres.
- On normal startup the API **migrates** the database and seeds the `member` and `admin` roles.
- Testing environment uses Postgres + in-memory OTP (`EnsureCreated`).

```bash
dotnet ef migrations add <Name> --project src/Aynera.Persistence --startup-project src/Aynera.Api --output-dir Migrations
```

---

## Auth behavior (short)

| Topic | Behavior |
|-------|----------|
| Member login | Phone or email → OTP (Redis hashed) **or** password → JWT access (1 hour) + opaque refresh (90 days, `aud=member`). Admin accounts are rejected as `user_not_found`. |
| Admin login | `POST /auth/admin/otp/request`, `/auth/admin/otp/verify`, `/auth/admin/password`. Phone or email → OTP or password → JWT access (1 hour) + refresh (24 hours, `aud=admin`). Member accounts are rejected as `user_not_found`. First admin is seeded from `AYNERA_ADMIN_*` (no public register) and is the super-admin. Additional admins created via `POST /admins/Create` are never super-admins. Any admin can list and open members at `GET /members/GetAll` and `GET /members/{id}`. Super-admins restrict/unrestrict members at `POST /members/{id}/restrict|unrestrict`. Inboxes (`page`/`pageSize`, default 15, max 50): `GET /early-access/signups/GetAll`, `/suggestions`, `/feedback`, `/members`. |
| Refresh | Rotated; reuse of an old refresh revokes the family |
| Access token | Bearer header; claims include `sub`, `aud`, `role`, `amr`, `auth_time`, `sid`, `jti` (admin tokens also include `is_super_admin`; authorization still reads the database) |
| `GET /members/me` | Requires JWT + **`Member`** policy (member role/audience and a current active, unrestricted Member account) |
| `GET /admins/me` | Requires JWT + **`Admin`** policy (admin role/audience and a current active, unrestricted Admin account). `isSuperAdmin` is read from the database. |
| Identity | ASP.NET Identity as **store**, not as login UI / `MapIdentityApi` (see ADR 002 in admin docs) |

---

## Middleware order

```text
CorrelationId → ExceptionHandling → (Swagger in Dev) → HTTPS → CORS → Authentication → CurrentUser → Authorization → Controllers
```

Details: `aynera-admin/docs/engineering/backend/middleware.md`.

---

## Local commands

```bash
# Dependencies
docker compose -f src/docker-compose.yml up -d postgres redis

# Run API
dotnet run --project src/Aynera.Api

# Tests
dotnet test src/Aynera.slnx
# or individual test projects under tests/
```

Open the solution from `src/Aynera.slnx`.

---

## Testing notes

| Project | Focus |
|---------|-------|
| `Aynera.Application.Tests` | Auth service + phone normalizer unit tests |
| `Aynera.Api.IntegrationTests` | HTTP register → OTP → verify → `/members/me` (`CapturingSmsService` / `CapturingEmailService`) |
| `Aynera.Domain.Tests` | Request validation helpers |

Integration tests use environment **`Testing`**: Postgres database `aynera_test` + in-memory OTP.  
Create `aynera_test` via docker-compose init (fresh volume) or `CREATE DATABASE aynera_test;` once. Postgres must be running.

---

## How to contribute safely

1. Match **Controller → Service → Repository** and project reference rules.
2. Put request FluentValidation next to Domain request DTOs (`Domain/<Feature>/Validators/`).
3. Prefer one repository per persistence concern.
4. Keep Domain free of EF/Identity types.
5. Extend [api-reference.md](./api-reference.md) when you add or change endpoints.
6. Prefer small, focused PRs; link the feature plan under `aynera-admin/docs/engineering/backend/` when one exists.

---

## Related repos

| Repo | Role |
|------|------|
| `aynera-web` | Public marketing site |
| `aynera-admin` | Staff admin + **all product documentation** |
| `aynera-app` | Member mobile (Expo) |

## Moderation consistency

UserManagementService now uses IWorkflowTransaction for restrict/unrestrict and admin activate/deactivate. Sorted account:{id:D} locks cover the actor and target; admin transitions also hold admins:lifecycle through count checks and commit. Super-admin eligibility is checked again inside the transaction using current account state. Self-deactivation remains prohibited.

Restriction/admin deactivation always revokes refresh sessions, including retries when the target is already restricted/inactive. Failures and cancellation roll back both state and session writes. Unrestriction does not reactivate a self-deactivated member; activation/unrestriction never restores revoked sessions. Audit remains best effort after commit and records actual state transitions only.

UserRepository identifier and ID projections use AsNoTracking so reads made before waiting on a workflow lock cannot supply stale Identity state to later writes. This also fixes the observed concurrent-recovery stale-concurrency-stamp failure.

OTP proof consumption is atomic per challenge key. AuthService and AccountLifecycleService call IOtpChallengeRepository.TryConsumeAsync, which validates purpose, audience, expiry and the hashed code and then either removes the challenge, increments attempts, or locks it. In-memory uses a per-key lock; Redis uses a Lua script so API instances cannot double-consume. A stale verifier cannot delete a replacement challenge. Failed attempts keep the existing TTL. Consumed proof is not restored if a later database write fails.

ModerationConsistencyTests covers persisted-write rollback, cancellation, retry, legacy session repair, enable rollback, independent self-deactivation, actor eligibility, concurrent cross-deactivation, and stale identifier reads. No route, DTO, database schema, or frontend changes are required.

## Live account-state authorization

AccountStateAuthorizationHandler reads an untracked user projection on every authorization check. Member and Admin policies require the matching account kind, active status, no restriction and no soft deletion. SuperAdmin combines this with its existing live privilege requirement. Default middleware responses are 401 for unauthenticated/invalid JWTs and 403 for denied state; a policy denial need not have an ApiResponse body.

MembersController uses MemberReactivation for authenticated recovery and MemberAccountDeletion for deletion. Both require an existing non-deleted member; lifecycle validation rejects restricted reactivation under the account lock and preserves its error contract. Deletion remains possible for inactive/restricted members. Anonymous OTP recovery and logout keep their existing contracts.

This is current-state enforcement, not permanent token invalidation or session validation. An unexpired token can become usable again after state restoration. Requests that passed authorization before a state change can finish. IssueMemberTokensAsync and RefreshTokenAsync now serialize session issuance with account disable operations as described below.

AccountStateAuthorizationTests uses real signed JWTs to verify that the same token loses normal-route access after inactive/restricted/deleted/missing/wrong-kind changes. It checks canonical and legacy admin URLs, member mutations, reactivation, and deletion exceptions. Policy unit tests cover state combinations and configured audiences.

## Token issuance and rotation consistency

AuthService uses IWorkflowTransaction and account:{userId:D} for shared login/OTP/password-reset session issuance and refresh rotation. Issuance rereads current account state and validates the configured audience against account kind under that lock. Credential verification, OTP consumption and login bookkeeping still precede this boundary. OTP consume/replay protection is provided by TryConsumeAsync before issuance starts.

Refresh first resolves the account lock from the opaque token hash, then rereads session/account state under the lock. Replacement creation and marking the parent replaced commit together. Concurrent uses create at most one replacement; the other call observes reuse and revokes the family, including that replacement. Clients must serialize refresh requests and sign in again on refresh_reuse.

Expected reuse/expiry/account-state denials return an internal outcome from the transaction, committing required revocation before the public method throws. Unexpected write errors or cancellation roll back the session changes. Success audit remains best effort after commit.

If issuance commits first, deactivation/restriction revokes its session. If disabling commits first, issuance is denied. A response can arrive after another request disables the account, but its session will be revoked and normal APIs apply current-state authorization. Permanent access-JWT invalidation remains separate work.

TokenIssuanceConsistencyTests covers both orderings for member deactivation/restriction and admin deactivation, concurrent refresh, after-write failures/cancellation/retry, and durable revocation on expected denials. No migration or API request/response shape change is required.
