# Solution structure

```text
elaris-api/
├── src/
│   ├── Elaris.Api/
│   ├── Elaris.Application/
│   ├── Elaris.Domain/
│   ├── Elaris.Infrastructure/
│   ├── Elaris.slnx
│   ├── Dockerfile
│   └── docker-compose.yml
├── tests/
├── docs/
└── README.md
```

Feature modules live under `src/Elaris.Application/Features/`.

Build / open the solution:

```bash
dotnet build src/Elaris.slnx
```
