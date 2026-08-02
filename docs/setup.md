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

## Run API (local SDK)

```bash
dotnet run --project src/Elaris.Api
```

Open the solution from `src/Elaris.slnx`.

## Run API in Docker

```bash
cd src
docker compose up -d --build
```

API: http://localhost:8080

Copy `.env.example` values into your local configuration as needed.
