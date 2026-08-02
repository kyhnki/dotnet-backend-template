# ADR-0001: Use a single-project Clean Architecture and Vertical Slice hybrid

- Status: Accepted
- Date: 2026-08-01
- Decision owners: Backend team

## Context

The starter targets MVPs built by small teams. A conventional four-project Clean Architecture solution provides compiler-enforced dependency boundaries, but it also spreads a small use case across multiple assemblies and adds project-reference ceremony before the product boundaries are stable. Pure Vertical Slice Architecture keeps related code together but does not by itself define where persistence and cross-cutting infrastructure belong.

The template needs one deployable service, short feature feedback loops, logical CQRS, SQL Server persistence, and a clear path to stronger boundaries if the application grows.

## Decision

Use one ASP.NET Core API project organized into four top-level areas:

- `Features` contains vertical slices grouped by business capability. A small slice keeps its endpoint, request, response, validator, handler, and mapping in one file.
- `Persistence` contains EF Core entities, configurations, migrations, and `AppDbContext`.
- `Infrastructure` contains authentication, authorization, and observability adapters.
- `Common` contains cross-cutting pipeline behaviors, exception handling, and composition extensions.

Use logical CQRS through separate MediatR commands and queries against one SQL Server database. Command handlers may modify tracked EF entities. Query handlers must use no-tracking projections into slice-owned response types. Do not add generic repositories, a custom unit of work, domain events, or separate read and write databases by default.

Tests remain separate projects because they are development tooling, not deployable architectural layers.

## Consequences

- A developer can understand and change a small use case in one place.
- The application has one deployment unit, one migration set, and a simple composition root.
- Folder and namespace conventions enforce most boundaries; the compiler does not prevent every cross-area dependency.
- The team must review slice boundaries and prevent `Common` from becoming a miscellaneous dumping ground.
- Domain, Application, and Infrastructure projects can be extracted later without changing the HTTP or use-case model when business complexity or team scale justifies it.

## Alternatives considered

- Four-project Clean Architecture was rejected for the MVP because its stronger compile-time boundaries did not justify the initial ceremony.
- Pure Vertical Slice Architecture without named persistence and infrastructure areas was rejected because operational concerns still need predictable ownership.
- A modular monolith with independent modules and schemas was deferred until genuine bounded contexts and independent team ownership emerge.
