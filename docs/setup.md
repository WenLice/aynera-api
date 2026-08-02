# Local setup

## Prerequisites

- .NET 8+ SDK
- Docker (PostgreSQL + Redis)

## Start dependencies

```bash
docker compose up -d
```

## Run API

```bash
dotnet run --project src/Elaris.Api
```

Copy `.env.example` values into your local configuration as needed.
