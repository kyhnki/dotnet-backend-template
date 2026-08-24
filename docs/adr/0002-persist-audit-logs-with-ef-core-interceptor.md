# ADR-0002: Persist opt-in audit logs with an EF Core interceptor

- Status: Accepted
- Date: 2026-08-01
- Decision owners: Backend team

## Context

The API needs a durable record of who changed persisted business data, what entity changed, and the values involved. Auditing in individual command handlers is easy to forget and can drift between slices. Logging changes only to Serilog or an external telemetry system does not guarantee that the audit record commits with the business transaction.

Not every EF entity should be audited, and indiscriminate value capture risks retaining secrets or personal data.

## Decision

Use a scoped EF Core `SaveChangesInterceptor` that creates `AuditLog` rows for entities implementing `IAuditableEntity`. Store the audit row in the same `SaveChanges` call and database transaction as the business change.

Each record contains the UTC timestamp, add/modify/delete action, entity type and ID, authenticated subject, trace ID, request path, and JSON old/new scalar values. Modified records contain only properties marked as changed. Properties decorated with `[AuditIgnore]` are excluded. `AuditLog` itself does not implement the marker and cannot recursively audit itself.

Auditable entities must have their primary keys assigned before `SaveChanges`; the template leaves `Id` unset and lets EF Core generate a sequential GUID client-side when the entity is added, as described in ADR-0006. Audit records are persistence data and are not exposed through a default HTTP endpoint.

## Consequences

- Feature handlers cannot accidentally omit auditing for an opted-in entity.
- Business data and its audit record commit or roll back together.
- The interceptor detaches audit rows staged by a previous failed attempt before creating new ones, so transient-failure retries do not duplicate audit records.
- Background operations can be audited, but their user and HTTP fields are null unless they establish equivalent execution context.
- JSON values make the schema stable across different entities but require database retention, access-control, and sensitive-data policies.
- Teams must apply `[AuditIgnore]` before adding secrets, tokens, large binary values, or unnecessary personal data to an auditable entity.
- Database-generated keys require a different post-save strategy and must not be introduced on auditable entities without revisiting this decision.

## Alternatives considered

- Handler-level audit calls were rejected because they are repetitive and easy to bypass.
- Serilog-only audit events were rejected because they do not share the database transaction.
- SQL Server temporal tables or CDC were not selected because they do not directly capture the authenticated application subject and request trace context.
