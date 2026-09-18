# aynera-api

ASP.NET Core modular monolith for **Aynera**.

- **Repo:** https://github.com/WenLice/aynera-api
- **Stack:** .NET 10, PostgreSQL, Redis
- **Host:** intended as `api.aynera.com`

## Layout

```text
aynera-api/
├── src/
│   ├── Aynera.Api/              # HTTP host
│   ├── Aynera.Application/      # Use cases / features
│   ├── Aynera.Domain/           # Entities and rules
│   ├── Aynera.Infrastructure/   # EF Core, Redis, integrations
│   ├── Aynera.slnx              # Solution file
│   ├── Dockerfile
│   └── docker-compose.yml
├── tests/
├── docs/
├── .env.example
└── README.md
```

| Path | Purpose |
|------|---------|
| `src/` | Application code, solution, Docker files |
| `tests/` | Automated tests |
| `docs/` | Developer guide, API reference, setup, structure |

Start with [docs/developer-guide.md](./docs/developer-guide.md) and [docs/api-reference.md](./docs/api-reference.md). Product-wide specs live in `aynera-admin/docs`.

## Requirements

- .NET 10 SDK
- Docker (optional, for Postgres / Redis / API container)

## Quick start

```bash
# dependencies
docker compose -f src/docker-compose.yml up -d postgres redis

# API
dotnet run --project src/Aynera.Api
```

Or run everything via Compose (from `src/`):

```bash
cd src
docker compose up -d --build
```

API listens on http://localhost:8080 when using the Compose `api` service.

See [docs/setup.md](./docs/setup.md).

## Related repos

| Repo | Role |
|------|------|
| `aynera-web` | Marketing website |
| `aynera-app` | Member mobile app |
| `aynera-admin` | Admin panel + product documentation |

## License

Proprietary — WenLice / Aynera. All rights reserved.
