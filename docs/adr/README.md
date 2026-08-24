# Architecture Decision Records

Architecture Decision Records (ADRs) capture decisions that materially affect the system structure, operational model, or development workflow. They explain why a decision was made so future contributors do not have to reconstruct its context from code or pull requests.

## Process

1. Copy `0000-template.md` and assign the next four-digit number.
2. Use a short kebab-case title, for example `0002-use-outbox-for-integration-events.md`.
3. Open the ADR as `Proposed` when discussion is still active.
4. Change its status to `Accepted` when the team agrees and merge it with the implementation when practical.
5. Never rewrite an accepted decision to change history. Add a new ADR and mark the old one `Superseded by ADR-NNNN`.

## Index

| ADR | Status | Decision |
|---|---|---|
| [0001](0001-use-single-project-hybrid-architecture.md) | Accepted | Use a single-project Clean Architecture and Vertical Slice hybrid |
| [0002](0002-persist-audit-logs-with-ef-core-interceptor.md) | Accepted | Persist opt-in audit logs with an EF Core interceptor |
| [0003](0003-use-time-provider-and-utc-timestamps.md) | Accepted | Use TimeProvider and require UTC timestamps |
| [0004](0004-localize-api-errors.md) | Accepted | Localize API errors through Accept-Language |
| [0005](0005-soft-delete-opt-in-entities.md) | Accepted | Soft-delete opt-in entities with a global query filter |
| [0006](0006-use-sequential-guid-keys.md) | Accepted | Use client-generated sequential GUID keys |
