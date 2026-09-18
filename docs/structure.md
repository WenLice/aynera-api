# Solution structure

```text
aynera-api/
├── src/
│   ├── Aynera.Domain/           # Requests/Responses/Records, enums, statics, FluentValidation
│   ├── Aynera.Application/      # services + repository interfaces + feature models (+ IEmail/ISms)
│   ├── Aynera.Persistence/      # entities, DbContext, EF migrations
│   ├── Aynera.Infrastructure/   # repository impl, Redis, JWT, seeds
│   ├── Aynera.Notifications/    # email/SMS adapters (Console, SMTP, Textbelt)
│   ├── Aynera.Api/              # controllers, middleware, composition root
│   ├── Aynera.slnx
│   ├── Dockerfile
│   └── docker-compose.yml
├── tests/
├── docs/
└── README.md
```

References (acyclic):

```text
Domain  ←  Application
Domain  ←  Persistence
Application + Persistence + Domain  ←  Infrastructure
Application  ←  Notifications
Application + Infrastructure + Notifications + Persistence + Domain  ←  Api
```

- **Domain:** per-feature `Requests/` / `Responses/` / `Records/` (one type per file), enums, statics, request FluentValidation  
- **Application:** feature services (`Services/Interfaces` + `Services/Implementations`), repository interfaces, config models  
- **Persistence:** entities, `AyneraDbContext`, migrations  
- **Controller → Service → Repository → Database**

Auth feature sketch:

```text
Application/Features/Auth/
  Models/
  Repositories/
  Services/Interfaces/
  Services/Implementations/
```

Build:

```bash
dotnet build src/Aynera.slnx
```

New migration:

```bash
dotnet ef migrations add <Name> --project src/Aynera.Persistence --startup-project src/Aynera.Api --output-dir Migrations
```

See [developer-guide.md](./developer-guide.md) and [api-reference.md](./api-reference.md).
