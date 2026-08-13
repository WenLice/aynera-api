# elaris-api — developer guide

Onboarding guide for the ElAris backend. Use this when you join the project or pick up backend work.

Product specs and feature plans live in **`elaris-admin/docs`** (especially `engineering/`). This repo keeps setup, structure, this guide, and the [API reference](./api-reference.md).

| Doc | Use when |
|-----|----------|
| [setup.md](./setup.md) | First local run |
| [structure.md](./structure.md) | Folder / project map |
| [api-reference.md](./api-reference.md) | Endpoint contracts for clients |
| [deferred-work.md](./deferred-work.md) | Later media/storage jobs — revisit when implementing |
| Architecture (admin docs) | Cross-repo architecture under `elaris-admin/docs/engineering/architecture.md` |
| Auth plan (admin docs) | `elaris-admin/docs/engineering/backend/auth/plan.md` |

---

## What this service is

`elaris-api` is an **ASP.NET Core modular monolith** for ElAris:

- Host intended as `api.elaris.com`
- Stack: **.NET 10**, **PostgreSQL**, **Redis**
- First live slice: **member phone OTP auth** (member-web; mobile audience shape reserved)
- Clients: `elaris-web`, `elaris-admin`, `elaris-app`

It is **not** a microservices fleet. Add features as folders under Application / Api, not as new deployables, unless an ADR says otherwise (`elaris-admin/docs/engineering/adr/001-modular-monolith.md`).

---

## Solution projects

| Project | Owns |
|---------|------|
| **Elaris.Domain** | Requests/Responses/Records, enums, static helpers, domain exceptions. **No** EF, Identity, or HTTP. |
| **Elaris.Application** | Feature services + **repository interfaces** + config models + notification contracts (`IEmailService`, `ISmsService`). |
| **Elaris.Persistence** | Entities (`AppUser`, `RefreshSession`), `ElarisDbContext`, EF migrations. |
| **Elaris.Infrastructure** | Repository implementations, Redis OTP, JWT signer, DI wiring for infra. |
| **Elaris.Notifications** | Email/SMS adapters (Console, SMTP, Textbelt) implementing Application contracts. |
| **Elaris.Api** | Controllers, middleware, Swagger, host `Program.cs`. |

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
Elaris.Application/Features/Auth/
├── Models/                 # JwtOptions, OtpOptions, token result records
├── Repositories/           # IUserRepository, IRefreshSessionRepository, IOtpChallengeRepository
└── Services/
    ├── Interfaces/         # IAuthService, ITokenService, ISmsService, IEmailService
    └── Implementations/    # AuthService

Elaris.Infrastructure/
├── Repositories/           # EF / Redis implementations
└── Services/               # JwtTokenService, CurrentUser, …

Elaris.Notifications/
├── Email/                  # ConsoleEmailService, SmtpEmailService
├── Sms/                    # ConsoleSmsService, TextbeltSmsService
└── DependencyInjection.cs  # AddElarisNotifications

Elaris.Api/Controllers/
├── AuthController.cs
└── UsersController.cs

Elaris.Domain/Auth/
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
Interactive docs (Development): Swagger UI at `/swagger`.

---

## Configuration

Bound mainly under `Elaris:*` in `appsettings.json` / environment:

| Area | Keys / notes |
|------|----------------|
| Postgres | `ConnectionStrings:Elaris` or `ELARIS_DB_CONNECTION` |
| Redis | `Elaris:Redis` / `ELARIS_REDIS` |
| JWT | `Elaris:Jwt` — signing key ≥ 32 chars; audiences `member-web`, `member-mobile`, `staff` |
| OTP | `Elaris:Otp` — length, TTL, attempt and rate caps |
| Email | `Elaris:Email` — `Provider` Console\|Smtp; SMTP host/from; verify link base URL |
| SMS | `Elaris:Sms` — `Provider` Console\|Textbelt; Textbelt key (free `textbelt`) |
| CORS | `Elaris:Cors:Origins` — local web/admin ports by default |
| Photos | `Elaris:Photos` — max count/bytes, stub face-match status |
| Introduction video | `Elaris:IntroductionVideo` — max bytes, banned words, stub transcript |
| Media authenticity | `Elaris:MediaAuthenticity` — AI-tool markers; reject synthetic photo/video |
| Early access | `Elaris:EarlyAccess` — IP rate limit (cities are DB-backed) |
| Feedback | `Elaris:Feedback` — grievance max length / IP rate limit |
| Suggestions | `Elaris:Suggestions` — idea max length / IP rate limit |

See `.env.example` and [setup.md](./setup.md).

**Media storage:** photos/videos use Postgres `bytea` temporarily (until dedicated server / object storage). Soft-delete space and account-delete archive job are deferred — see [deferred-work.md](./deferred-work.md).

**Email / SMS:** live in **Elaris.Notifications** behind `IEmailService` / `ISmsService`. `Provider` = `Console` (default), `Smtp`, or `Textbelt`. Never logs OTP, phones, emails, or verify URLs. Api registers `AddElarisNotifications`. Integration tests replace with capturing doubles.

---

## Database and migrations

- Schema is **EF Core code-first**. Do not hand-create app tables in Postgres.
- On normal startup the API **migrates** the database and seeds the `member` role.
- Testing environment uses Postgres + in-memory OTP (`EnsureCreated`).

```bash
dotnet ef migrations add <Name> --project src/Elaris.Persistence --startup-project src/Elaris.Api --output-dir Migrations
```

---

## Auth behavior (short)

| Topic | Behavior |
|-------|----------|
| Member login | +91 phone → OTP in Redis (hashed) → JWT access + opaque refresh |
| Refresh | Rotated; reuse of an old refresh revokes the family |
| Access token | Short-lived; Bearer header; claims include `sub`, `aud`, `role`, `amr`, `auth_time`, `sid`, `jti` |
| `GET /users/me` | Requires JWT + **`Member`** policy |
| Identity | ASP.NET Identity as **store**, not as login UI / `MapIdentityApi` (see ADR 002 in admin docs) |

---

## Middleware order

```text
CorrelationId → ExceptionHandling → (Swagger in Dev) → HTTPS → CORS → Authentication → CurrentUser → Authorization → Controllers
```

Details: `elaris-admin/docs/engineering/backend/middleware.md`.

---

## Local commands

```bash
# Dependencies
docker compose -f src/docker-compose.yml up -d postgres redis

# Run API
dotnet run --project src/Elaris.Api

# Tests
dotnet test src/Elaris.slnx
# or individual test projects under tests/
```

Open the solution from `src/Elaris.slnx`.

---

## Testing notes

| Project | Focus |
|---------|-------|
| `Elaris.Application.Tests` | Auth service + phone normalizer unit tests |
| `Elaris.Api.IntegrationTests` | HTTP register → OTP → verify → `/users/me` (`CapturingSmsService` / `CapturingEmailService`) |
| `Elaris.Domain.Tests` | Request validation helpers |

Integration tests use environment **`Testing`**: Postgres database `elaris_test` + in-memory OTP.  
Create `elaris_test` via docker-compose init (fresh volume) or `CREATE DATABASE elaris_test;` once. Postgres must be running.

---

## How to contribute safely

1. Match **Controller → Service → Repository** and project reference rules.
2. Put request FluentValidation next to Domain request DTOs (`Domain/<Feature>/Validators/`).
3. Prefer one repository per persistence concern.
4. Keep Domain free of EF/Identity types.
5. Extend [api-reference.md](./api-reference.md) when you add or change endpoints.
6. Prefer small, focused PRs; link the feature plan under `elaris-admin/docs/engineering/backend/` when one exists.

---

## Related repos

| Repo | Role |
|------|------|
| `elaris-web` | Public marketing site |
| `elaris-admin` | Staff admin + **all product documentation** |
| `elaris-app` | Member mobile (Expo) |
