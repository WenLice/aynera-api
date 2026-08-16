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

Copy `.env.example` values into your environment or `appsettings.Development.json` as needed (`ELARIS_DB_CONNECTION`, `ELARIS_REDIS`, `ELARIS_JWT_SIGNING_KEY`).

On startup the API applies EF Core migrations, seeds the `member` and `admin` roles, and creates the first admin if `ELARIS_ADMIN_EMAIL` and `ELARIS_ADMIN_PASSWORD` are set and no admin exists yet. That first seeded admin is the super-admin. Admins created later via `POST /admin/admins` are never super-admins.

Migrations live in `Elaris.Persistence`:

```bash
dotnet ef migrations add <Name> --project src/Elaris.Persistence --startup-project src/Elaris.Api --output-dir Migrations
```

## Run API (local SDK)

```bash
dotnet run --project src/Elaris.Api
```

- Health: `GET /health`
- Auth: `POST /auth/verifyemail`, `POST /auth/login`, `POST /auth/verifysms`, `POST /auth/password`, `POST /auth/password/forgot`, `POST /auth/password/reset`, `POST /auth/refresh`, `POST /auth/logout`
- Admin: `POST /admin/otp/request`, `POST /admin/otp/verify`, `POST /admin/password`, `GET /admin/me`, `GET /admin/members?page=&pageSize=`, `GET /admin/members/{id}`, `GET /admin/members/{id}/photos/{photoId}`, `GET /admin/members/{id}/introduction-video/content`, `POST /admin/members/{id}/restrict`, `POST /admin/members/{id}/unrestrict`, `GET /admin/admins`, `POST /admin/admins`, `POST /admin/admins/{id}/deactivate`, `POST /admin/admins/{id}/activate`, `GET /admin/suggestions?page=&pageSize=`, `GET /admin/feedback?page=&pageSize=`
- Early access: `GET /early-access/cities`, `GET /early-access/signups?page=&pageSize=` (Admin), `POST /early-access/register`, `POST`/`PATCH`/`DELETE /early-access/cities` (Admin)
- Users: `POST /users/register`, `GET /users/me`, `POST /users/me/password`, `POST /users/me/photos`, `GET /users/me/photos`, `GET /users/me/photos/{id}`, `DELETE /users/me/photos/{id}`, `POST /users/me/introduction-video`, `GET /users/me/introduction-video`, `GET /users/me/introduction-video/content`, `DELETE /users/me/introduction-video`, `POST /users/deactivate`, `POST /users/reactivate`, `DELETE /users/account`
- Controllers inherit `BaseController` (`IControllerBase`) and call **services**
- Services own business logic; **repositories** own per-model persistence
- Responses use `ApiResponse<T>`
- Swagger UI (Development): http://localhost:5057/swagger
- Dev OTP: not printed to logs. Integration tests capture codes via `CapturingSmsService` / `CapturingEmailService`. For manual local login, use those tests or a temporary real SMS/email provider.

Open the solution from `src/Elaris.slnx`.

## Run API in Docker

```bash
cd src
docker compose up -d --build
```

API: http://localhost:8080

## Tests

```bash
dotnet test src/Elaris.slnx
```
