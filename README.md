# elaris-api

ASP.NET Core modular monolith for ElAris.

## Solution

| Project | Role |
|---------|------|
| `Elaris.Api` | HTTP + SignalR host |
| `Elaris.Application` | Use cases / features |
| `Elaris.Domain` | Entities, enums, rules |
| `Elaris.Infrastructure` | EF Core, Redis, S3, SMS, email, Hangfire |

## Run locally

```bash
docker compose up -d
dotnet run --project src/Elaris.Api
```

## Docs

Product and engineering specs live in **`elaris-admin/docs`**. This repo keeps setup notes only.
