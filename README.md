# elaris-api

ASP.NET Core modular monolith for **ElAris**.

- **Repo:** https://github.com/WenLice/elaris-api
- **Stack:** .NET 10, PostgreSQL, Redis
- **Host:** intended as `api.elaris.com`

## Layout

```text
elaris-api/
├── src/
│   ├── Elaris.Api/              # HTTP host
│   ├── Elaris.Application/      # Use cases / features
│   ├── Elaris.Domain/           # Entities and rules
│   ├── Elaris.Infrastructure/   # EF Core, Redis, integrations
│   ├── Elaris.slnx              # Solution file
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

Start with [docs/developer-guide.md](./docs/developer-guide.md) and [docs/api-reference.md](./docs/api-reference.md). Product-wide specs live in `elaris-admin/docs`.

## Requirements

- .NET 10 SDK
- Docker (optional, for Postgres / Redis / API container)

## Quick start

```bash
# dependencies
docker compose -f src/docker-compose.yml up -d postgres redis

# API
dotnet run --project src/Elaris.Api
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
| `elaris-web` | Marketing website |
| `elaris-app` | Member mobile app |
| `elaris-admin` | Admin panel + product documentation |

## License

Proprietary — WenLice / ElAris. All rights reserved.
