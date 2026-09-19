# Local setup

## Prerequisites

- .NET 10 SDK
- Docker (PostgreSQL + Redis)

## Start dependencies

From the repo root:

```bash
docker compose -f src/docker-compose.yml up -d postgres redis
```

Or from `src/`:

```bash
cd src
docker compose up -d postgres redis
```

## Configure

Copy `.env.example` values into your environment or `appsettings.Development.json` as needed (`AYNERA_DB_CONNECTION`, `AYNERA_REDIS`, `AYNERA_JWT_SIGNING_KEY`).

On startup the API applies EF Core migrations, seeds the `member` and `admin` roles, and creates the first admin if `AYNERA_ADMIN_EMAIL` and `AYNERA_ADMIN_PASSWORD` are set and no admin exists yet. That first seeded admin is the super-admin. Admins created later via `POST /admins/Create` are never super-admins.

Migrations live in `Aynera.Persistence`:

```bash
dotnet ef migrations add <Name> --project src/Aynera.Persistence --startup-project src/Aynera.Api --output-dir Migrations
```

## Run API (local SDK)

```bash
dotnet run --project src/Aynera.Api
```

- Health: `GET /health`
- Auth: `POST /auth/verifyemail`, `POST /auth/otp/request`, `POST /auth/otp/verify`, `POST /auth/password`, `POST /auth/password/forgot`, `POST /auth/password/reset`, `POST /auth/refresh`, `POST /auth/logout`
- Admin: `POST /auth/admin/otp/request`, `POST /auth/admin/otp/verify`, `POST /auth/admin/password`, `GET /admins/me`, `GET /members/GetAll?page=&pageSize=`, `GET /members/{id}`, `GET /photos/{id}/{photoId}`, `GET /introduction-video/{id}/content`, `POST /members/{id}/restrict`, `POST /members/{id}/unrestrict`, `GET /admins/GetAll`, `POST /admins/Create`, `POST /admins/{id}/deactivate`, `POST /admins/{id}/activate`, `GET /suggestions/GetAll?page=&pageSize=`, `GET /feedback/GetAll?page=&pageSize=`
- Early access: `GET /early-access/cities/GetAll`, `GET /early-access/signups/GetAll?page=&pageSize=` (Admin), `POST /early-access/register`, `POST /early-access/cities/Create`, `PATCH`/`DELETE /early-access/cities/{id}` (Admin)
- Members: `POST /members/register`, `GET /members/me`, `POST /members/me/password`, `POST /members/me/deactivate`, `POST /members/me/reactivate`, `POST /members/reactivate/request`, `POST /members/reactivate/recover`, `DELETE /members/me`
- Photos: `POST /photos/Upload`, `GET /photos/GetAll`, `GET /photos/{photoId}`, `DELETE /photos/{photoId}`; staff `GET /photos/{id}/{photoId}`
- Introduction video: `POST /introduction-video/Upload`, `GET /introduction-video/me`, `GET /introduction-video/me/content`, `DELETE /introduction-video/me`; staff `GET /introduction-video/{id}/content`
- Venues (Admin): `GET /venues/GetAll`, `POST /venues/Create`, `PATCH`/`DELETE /venues/{id}`, `POST /venues/{id}/notifications/Create`, `GET /venues/{id}/notifications/GetAll`
- Admissions: `GET /admissions/me`, `POST /admissions/me/submit`, `POST /admissions/me/consents` (Member); `GET /admissions/GetAll`, `GET /admissions/{userId}`, `POST /admissions/{userId}/decision`, `GET /admissions/{userId}/eligibility` (Admin)
- Route convention: list actions end in `/GetAll`, collection POSTs carry an action segment (`/Create`, `/Upload`, `/register`); a bare controller prefix is never a route.
- Controllers inherit `BaseController` (`IControllerBase`) and call **services**
- Services own business logic; **repositories** own per-model persistence
- Responses use `ApiResponse<T>`
- Swagger UI (Development): http://localhost:5057/swagger
- Dev OTP: not printed to logs. Integration tests capture codes via `CapturingSmsService` / `CapturingEmailService`. For manual local login, use those tests or a temporary real SMS/email provider.

Open the solution from `src/Aynera.slnx`.

## Run API in Docker

```bash
cd src
docker compose up -d --build
```

API: http://localhost:8080

## Tests

For a native Windows PostgreSQL 18 installation, create the dedicated test database
once from PowerShell using the PostgreSQL administrator account:

```powershell
& 'C:/Program Files/PostgreSQL/18/bin/psql.exe' -X -W -h localhost -U postgres -d postgres -f 'scripts/create-test-db.sql'
```

Run this from the backend repository root. Enter the administrator password at the
prompt. The script creates `aynera_test` owned by the existing `aynera` role and
leaves an existing database untouched. It does not grant `CREATEDB` to the application
role. Change the executable path if a different PostgreSQL version is installed.

The integration test host applies EF Core migrations and clears application tables
in `aynera_test`, preserving `__EFMigrationsHistory` for subsequent runs.
Use this database only for tests. The default connection uses localhost port 5432
and the `aynera` role; set `AYNERA_TEST_DB_CONNECTION` when those connection details
differ. The fixture always targets the database named `aynera_test`.

```bash
dotnet test src/Aynera.slnx
```
