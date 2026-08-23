# Basic Architecture

## Purpose

This repository has one deployable API assembly. Clean Architecture is applied as a small set of dependency rules inside that assembly; vertical slices define the unit of change.

```text
HTTP endpoint
    -> MediatR command/query
        -> validation and logging behaviors
            -> handler
                -> AppDbContext
                    -> SQL Server
```

```text
src/DotNetBackendTemplate.Api
  Features/         cohesive use cases grouped by business area
  Persistence/      EF entities, mappings, migrations, and DbContext
  Infrastructure/   authentication, authorization, and observability adapters
  Common/           cross-cutting pipeline, errors, and registration
```

Tests are separate assemblies but are not deployable services.

## Rules

- Endpoints bind HTTP inputs, dispatch a request, and map the result. They do not contain application logic.
- A small slice stays in one file with its endpoint, request, validator, response, and handler.
- Command handlers may mutate tracked EF entities and call `SaveChangesAsync`.
- Query handlers use `AsNoTracking` and project directly to query-owned response models.
- Handlers may inject `AppDbContext` directly. EF Core is already the repository and unit of work.
- EF entities never cross the HTTP boundary.
- Validation runs in the MediatR pipeline and becomes RFC-style Validation Problem Details whose error keys are camelCase, matching the JSON request fields.
- Expected absence is represented explicitly, such as a nullable query response mapped to `404`.
- Unexpected exceptions are logged centrally and returned as non-sensitive `500` Problem Details with a trace ID.
- Role names and policies are centralized; endpoints refer to policy constants instead of raw role strings.
- Persistence auditing is opt-in through `IAuditableEntity`; audit rows commit with the business change through the EF Core interceptor.
- Sensitive or oversized properties on auditable entities must use `[AuditIgnore]`.
- Soft delete is opt-in through `ISoftDeletable`; its `!IsDeleted` query filter is applied by convention, deletes become updates, and the audit trail still records them as `Deleted`. Unique indexes on such entities must be filtered, and reads that need deleted rows use `IgnoreQueryFilters()`.
- Code that needs the current instant injects `TimeProvider` and calls `GetUtcNow`; direct system-clock APIs are rejected by analyzers.
- API, entity, and database timestamps use `DateTimeOffset` with a UTC offset. User-local display and timezone conversion belong to clients unless a business rule explicitly requires timezone-aware scheduling.
- Client-facing validation and Problem Details text comes from shared localization resources. Machine-readable fields, logs, persisted data, and OpenAPI metadata remain culture-neutral.
- The built-in OpenAPI document is the development-time API contract. Endpoint authorization metadata drives JWT security requirements; anonymous endpoints must remain explicitly marked.
- Credentials, identity settings, telemetry endpoints, and license keys come from external configuration.

## Deliberately excluded

The MVP does not include aggregates, domain events, generic repositories, a custom unit of work, an event bus, separate read and write databases, Redis, background jobs, or Aspire. Add one only after a concrete requirement makes its cost worthwhile.

## Growth path

Keep code in its owning slice until real duplication or coupling appears. If the product develops complex business invariants, extract domain concepts first. If infrastructure dependencies or team ownership become difficult to control, split Domain, Application, and Infrastructure into projects while retaining the same feature-oriented use cases. Physical CQRS, messaging, and additional storage should remain later operational decisions rather than template defaults.
