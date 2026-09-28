# .NET Backend Template Starter

A pragmatic .NET 10 starter for small teams building an MVP. It uses one deployable ASP.NET Core project, vertical slices, logical CQRS, MediatR, EF Core, SQL Server, FluentValidation, Serilog, OpenTelemetry, and JWT bearer authentication.

The included `Campaigns` feature is a removable example. It demonstrates a command, focused query projections, validation, pagination, persistence, and endpoint tests without introducing repositories, domain events, or a separate domain assembly.

## Prerequisites

- .NET 10 SDK
- Docker Desktop or another Docker-compatible runtime
- An OpenID Connect provider when calling protected endpoints

## Quick start

Copy the local SQL Server settings and choose a strong development password:

```bash
cp .env.example .env
docker compose up -d sqlserver
```

Store the matching connection string outside source control:

```bash
dotnet user-secrets set \
  --project src/DotNetBackendTemplate.Api \
  "ConnectionStrings:Database" \
  "Server=localhost,1433;Database=DotNetBackendTemplate;User Id=sa;Password=Change_this_local_password_123!;Encrypt=True;TrustServerCertificate=True"
```

Create the database and start the API:

```bash
dotnet ef database update \
  --project src/DotNetBackendTemplate.Api \
  --startup-project src/DotNetBackendTemplate.Api

dotnet run --project src/DotNetBackendTemplate.Api
```

The development profile listens on `http://localhost:5080`. Anonymous operational endpoints are available at:

- `GET /health/live`
- `GET /health/ready`
- `GET /openapi/v1.json` in Development
- `GET /docs` for Swagger UI in Development

## OpenAPI specification

The API uses the built-in .NET 10 OpenAPI generator and exposes its OpenAPI 3 document at `GET /openapi/v1.json` in Development. Integration tests also enable the document endpoint in the `Testing` environment; it is not mapped in Production.

The document includes API title/version metadata, campaign and health operations, request and response schemas, and an HTTP bearer security scheme named `Bearer`. Contract types nested in a slice are named after it, such as `GetCampaignResponse` and `CreateCampaignRequest`, so each slice's `Request`, `Response`, and `Item` gets its own schema. `OpenApiContractTests` fails if an operation's documented schema stops matching the type the endpoint binds or returns. Security requirements are derived from endpoint authorization metadata, so protected operations declare JWT authentication while explicitly anonymous endpoints do not.

Swagger UI is available at `http://localhost:5080/docs` in Development and uses the existing Microsoft-generated document; the project does not register Swashbuckle's document generator. To call a protected operation, select **Authorize**, paste the raw JWT access token issued by the configured identity provider, and then execute the request. Do not include the `Bearer` prefix in the token field. Authorization is not persisted by the UI, and neither the UI nor the OpenAPI document is exposed in Production.

When adding an endpoint, always provide a stable `.WithName(...)`, tag, summary, description, response metadata, and the correct `.RequireAuthorization(...)` or `.AllowAnonymous()` metadata.

## Authentication

The API validates access tokens but does not issue them or manage users. Outside the Development environment, startup fails when `Authentication:Authority` or `Authentication:Audience` is missing, so misconfigured deployments surface at boot instead of as runtime `401`s. Configure the authority, audience, and claim names for your identity provider:

```bash
dotnet user-secrets set --project src/DotNetBackendTemplate.Api "Authentication:Authority" "https://identity.example.com"
dotnet user-secrets set --project src/DotNetBackendTemplate.Api "Authentication:Audience" "dotnet-backend-template-api"
dotnet user-secrets set --project src/DotNetBackendTemplate.Api "Authentication:RoleClaimType" "role"
```

All `/api/v1/campaigns` routes require a bearer token. Creating a campaign additionally requires either the `Administrator` or `CampaignManager` role:

```bash
curl -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  http://localhost:5080/api/v1/campaigns
```

The starter intentionally pins MediatR 12.5.0, the final Apache-licensed release before the license-key model introduced in version 13. Review the current MediatR licensing terms before upgrading beyond 12.5.0.

Role values and policy names are centralized under `Infrastructure/Authorization`. Adapt them to the vocabulary and claim shape of the selected identity provider rather than scattering role strings across endpoints.

## Campaign example

```text
POST   /api/v1/campaigns               Administrator or CampaignManager
PUT    /api/v1/campaigns/{id}          Administrator or CampaignManager
DELETE /api/v1/campaigns/{id}          Administrator or CampaignManager
GET    /api/v1/campaigns/{id}
GET    /api/v1/campaigns?page=1&pageSize=20
```

Each operation lives in one slice file under `Features/Campaigns`. Commands modify tracked EF entities. Queries always use `AsNoTracking()` and project only the response fields needed by that endpoint. The list endpoint normalizes the page to at least `1` and clamps page size to `1..100`.

Campaign timestamps must include the UTC offset `+00:00`. The API does not silently normalize local or non-UTC timestamps.

## UTC time and localization

Inject .NET's `TimeProvider` whenever code needs the current instant and call `GetUtcNow()`. Production uses `TimeProvider.System`, while tests can replace it with a fixed or controllable provider. Repository analyzers reject direct use of `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, and `DateTimeOffset.UtcNow`.

The API supports English (default) and Turkish error messages. Both specific and neutral language tags are accepted (`en-US`, `en`, `tr-TR`, `tr`). Select a language with the standard `Accept-Language` header:

```bash
curl -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Accept-Language: tr-TR" \
  -H "Content-Type: application/json" \
  --data '{"name":"","startsAtUtc":"2026-08-01T10:00:00+03:00","endsAtUtc":"2026-08-02T10:00:00+03:00"}' \
  http://localhost:5080/api/v1/campaigns
```

Only validation messages and Problem Details titles are localized. JSON fields, validation keys, logs, OpenAPI metadata, persisted data, and ISO 8601 UTC timestamps remain culture-neutral. Unsupported or missing languages fall back to English. User-local display time and timezone conversion belong to clients unless a future business rule requires timezone-aware scheduling.

## Adding a slice

1. Add one operation file under `Features/<Area>`.
2. Keep its HTTP request, MediatR request, response, validator, handler, and endpoint mapping together.
3. Register its mapping in `EndpointRouteBuilderExtensions`.
4. Put every new client-facing validation message in the shared English and Turkish resources.
5. Inject `TimeProvider` instead of reading the system clock when the slice creates timestamps.
6. Add focused validator tests and end-to-end API tests.
7. Split a large slice into a same-named folder only when the single file stops being readable.

Do not return EF entities from endpoints. Do not add a generic repository or unit of work over EF Core. Keep business logic out of endpoint delegates, and keep request payloads out of logs.

## Database migrations

The application never migrates automatically on startup. Use explicit commands during development and deployment:

```bash
dotnet ef migrations add MigrationName \
  --project src/DotNetBackendTemplate.Api \
  --startup-project src/DotNetBackendTemplate.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/DotNetBackendTemplate.Api \
  --startup-project src/DotNetBackendTemplate.Api
```

The design-time factory reads the database connection from user secrets or `ConnectionStrings__Database`.

## Persistence audit logs

EF Core writes an `AuditLog` row in the same transaction whenever an entity implementing `IAuditableEntity` is added, modified, or deleted. `Campaign` opts in by default. Records include the action, entity and key, authenticated subject, trace ID, request path, and JSON old/new values.

Use `[AuditIgnore]` on secrets, tokens, large values, and personal data that does not belong in the audit trail. Auditable entities must have their keys assigned before `SaveChanges`. Leave `Id` unset on new entities: EF Core generates a sequential GUID client-side when the entity is added, which keeps the key available for auditing and stops the clustered primary key from fragmenting. Do not reintroduce `Guid.NewGuid()` or a database-generated key on an auditable entity — see [ADR-0006](docs/adr/0006-use-sequential-guid-keys.md). Audit records are intentionally not exposed through an API endpoint. Define retention rules and restrict direct table access before production use.

## Soft delete

Entities opt into soft delete by implementing `ISoftDeletable`. A global query filter is applied to them by convention, so a deleted row disappears from every query without feature handlers doing anything. `Campaign` opts in by default; `DELETE /api/v1/campaigns/{id}` marks the row deleted and returns `204`, and a second delete returns `404`.

An EF Core interceptor turns `Remove` into an update that sets `IsDeleted` and `DeletedAtUtc`. The audit trail still records the change as `Deleted`, because the audit action is derived from `IsDeleted` changing rather than from EF's entity state — which also keeps it correct when a transient failure retries the save.

Read deleted rows with `IgnoreQueryFilters()`. Put `HasFilter("[IsDeleted] = 0")` on any unique index of a soft-deletable entity, or a deleted row will permanently reserve its unique value. Soft delete is not erasure: a genuine "delete my data" request needs a hard delete, and deleted rows need a retention policy.

## Docker Compose

Compose defines both the API and SQL Server. Migrations remain an explicit operation: start SQL Server, apply migrations from the host, and then start the complete stack.

```bash
docker compose up -d sqlserver
dotnet ef database update \
  --project src/DotNetBackendTemplate.Api \
  --startup-project src/DotNetBackendTemplate.Api
docker compose up -d --build api
```

Set identity-provider and optional telemetry values in `.env`. The API is exposed at `http://localhost:5080`; SQL Server is exposed at `localhost:1433`.

## Tests

```bash
dotnet test DotNetBackendTemplate.slnx
```

Tests use xUnit with Fluent Assertions 7.2.2. Unit tests exercise validation without infrastructure. Integration tests use `WebApplicationFactory`, a replaceable test authentication scheme, the real EF migrations, and a disposable SQL Server 2022 Testcontainer. Docker must be running for integration tests.

Architecture tests in `tests/DotNetBackendTemplate.UnitTests/Architecture` fail when code breaks a rule in [architecture rules](docs/architecture-rules.md), and each failure names the rule ID. They need no Docker. Each check is also run against a deliberately broken fixture application to prove it still catches the violation it exists for.

## CI/CD

This starter intentionally does not include a provider-specific CI/CD workflow. Add one after cloning that matches the product's hosting, secrets, migration, and deployment requirements. At minimum, automate restore, formatting verification, a warnings-as-errors build, and all tests.

## Observability

Serilog provides structured console and request logs. OpenTelemetry instruments ASP.NET Core, `HttpClient`, and runtime metrics. Set `OpenTelemetry:OtlpEndpoint` or `OpenTelemetry__OtlpEndpoint` to enable OTLP trace and metric export; no exporter is started when the value is empty.

## Clone-and-rename checklist

- Rename `DotNetBackendTemplate.slnx`, projects, namespaces, assembly names, and the user-secrets ID.
- Rename the SQL Server database, Compose project, volume, and container-facing settings.
- Replace or remove `Features/Campaigns` and its tests.
- Configure the real identity provider, audience, authorization policies, telemetry destination.
- Review package versions, secret management, data retention, and deployment migration strategy.

See the [architecture overview](docs/basic-architecture.md), [architecture rules and review checklist](docs/architecture-rules.md), and [architecture decision records](docs/adr/README.md) for the starter's structure, dependency boundaries, and accepted design decisions.
