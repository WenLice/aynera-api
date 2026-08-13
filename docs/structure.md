# Solution structure

```text
elaris-api/
├── src/
│   ├── Elaris.Domain/           # Requests/Responses/Records, enums, statics, FluentValidation
│   ├── Elaris.Application/      # services + repository interfaces + feature models (+ IEmail/ISms)
│   ├── Elaris.Persistence/      # entities, DbContext, EF migrations
│   ├── Elaris.Infrastructure/   # repository impl, Redis, JWT, seeds
│   ├── Elaris.Notifications/    # email/SMS adapters (Console, SMTP, Textbelt)
│   ├── Elaris.Api/              # controllers, middleware, composition root
│   ├── Elaris.slnx
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
- **Persistence:** entities, `ElarisDbContext`, migrations  
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
dotnet build src/Elaris.slnx
```

New migration:

```bash
dotnet ef migrations add <Name> --project src/Elaris.Persistence --startup-project src/Elaris.Api --output-dir Migrations
```

See [developer-guide.md](./developer-guide.md) and [api-reference.md](./api-reference.md).
