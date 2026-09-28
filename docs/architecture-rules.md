# Architecture Rules

## Purpose and scope

This document is the review contract for this starter: one deployable ASP.NET Core API, vertical slices, logical CQRS, and one SQL Server database. It translates the [architecture overview](basic-architecture.md) and [accepted ADRs](adr/README.md) into placement and dependency rules.

These are conventions inside one assembly, not compiler-enforced project boundaries. Do not introduce separate Domain/Application/Infrastructure projects just to satisfy a generic Clean Architecture diagram. Direct `AppDbContext` injection into handlers is intentional.

Rules apply to production code under `src/DotNetBackendTemplate.Api`. Test fixtures may reference production areas and replace infrastructure. Generated migrations remain subject to schema review but are excluded from hand-written-code placement checks.

**Enforcement status:** this document does not add architecture tests or analyzers. Clock API restrictions already have analyzer enforcement, and existing tests cover selected runtime contracts. Structural checks described below are candidates for future automation; passing today's tests does not prove architectural compliance.

## 1. Decide ownership by responsibility

Ask: **does this code decide what the product allows, implement a technical mechanism, or wire components together?** A class name ending in `Service` does not answer that question.

| Area | Owns | Must not become |
| --- | --- | --- |
| `Features/<Area>` | Use cases, commands/queries, handlers, request/response models, input validation, endpoint mapping, feature-specific business decisions | Transport-only wrappers around business logic hidden elsewhere |
| `Persistence` | EF entities, mappings, migrations, `AppDbContext`, audit and soft-delete mechanisms | A location for use-case orchestration or campaign eligibility rules |
| `Infrastructure` | JWT integration, authorization configuration, localization infrastructure, OpenAPI, observability, future external-system adapters | A home for product workflows merely because they call a database or external API |
| `Common/Behaviors` | Generic validation and logging around MediatR requests | Feature-specific checks or state transitions |
| `Common/Exceptions` | Shared exception-to-Problem-Details handling | Feature workflows or database operations |
| `Program.cs`, `Common/Extensions` | Composition: service registration, middleware and endpoint wiring, operational health endpoints | Business decisions disguised as registration helpers |

Keep feature helpers in their owning feature until there is genuine shared responsibility. Do not move code to `Common` merely because two files call it. Large slices may become same-named folders containing top-level types; responsibility rules do not depend on nested types or a single-file layout.

### Placement examples

| Responsibility | Location |
| --- | --- |
| Verify JWT issuer, audience, and signature | `Infrastructure/Authentication` |
| Decide whether a campaign may be published | `Features/Campaigns` |
| Send a message using an email provider | An adapter under `Infrastructure` |
| Decide when a campaign notification is required | `Features/Campaigns` |
| Map a campaign property to a database column | `Persistence/Configurations` |
| Record old/new values for opted-in entities | `Persistence/Auditing` |
| Decide whether a particular campaign may be deleted | `Features/Campaigns`, not the soft-delete interceptor |

Email and campaign publishing are illustrative examples, not capabilities currently supplied by the starter.

## 2. Dependency boundaries

Dependencies include constructor parameters, inheritance, implemented interfaces, method signatures, generic arguments, attributes, and calls inside method bodies. Searching only `using` statements is insufficient.

| ID | Rule | Verification |
| --- | --- | --- |
| DEP-01 | `Infrastructure` must not depend on `Features` types. | Type/member dependency analysis |
| DEP-02 | `Persistence` must not depend on `Features` types. Entities must not refer to HTTP requests/responses or MediatR handlers. | Dependency analysis and review |
| DEP-03 | `Common/Behaviors` and `Common/Exceptions` must not depend on concrete feature types or `AppDbContext`. Shared localization resources are allowed. | Dependency analysis |
| DEP-04 | Feature handlers may depend on `AppDbContext`, persistence entities, and technical capabilities required by the use case. This is intentional under this template's architecture. | Review |
| DEP-05 | A feature must not directly call or instantiate another feature's handler, or reuse another feature's HTTP DTO as its own contract. Cross-feature coordination needs explicit design review; sending another request is not an automatic exemption. | Dependency analysis plus workflow review |
| DEP-06 | Production types must never depend on test assemblies. | Project/reference analysis |
| DEP-07 | Hand-written source folders and namespaces must agree. Renaming a namespace must not hide a class physically stored in the wrong area. | Source analysis |

### Explicit current exceptions and allowances

- `Common/Extensions/ServiceCollectionExtensions.cs` references persistence and infrastructure to register services. `EndpointRouteBuilderExtensions.cs` references features to map routes. These are composition code, so do **not** apply DEP-03 to all of `Common`.
- Authorization policy names and role constants currently live in `Infrastructure/Authorization`; `AuthenticationExtensions` registers the role-based `CampaignWrite` policy. This matches the existing architecture guidance and is an accepted centralized configuration convention, not an instruction to move those files now. Do not grow it into campaign state, ownership, quota, or eligibility logic. Moving feature-owned policy definitions later requires updating this convention and related documentation together.
- Shared `ApiResources` and their resource files are centralized, including feature validation messages. A resource key containing `Campaign` is not by itself a misplaced business rule.
- The audit interceptor reads HTTP execution context to capture actor and trace metadata. Do not impose a blanket ban on ASP.NET Core types throughout `Persistence`; entities still must not depend on HTTP concerns.
- Infrastructure access to persistence is not automatically a business-rule violation. A future storage-backed technical adapter needs a documented reason and review of its operations. Conversely, injecting only primitive values does not prove that infrastructure code is free of business logic.

Keep exceptions specific to a responsibility or type. Do not exclude an entire namespace just to make a failing check pass.

## 3. Vertical slices and logical CQRS

- **SLICE-01:** Application `IRequestHandler` implementations belong under `Features/<Area>`. Validators for feature commands/queries belong with the owning feature. Configuration validators, if introduced, are not feature validators.
- **SLICE-02:** Endpoint delegates bind input, construct and dispatch a request, and map its outcome to HTTP. Do not query the database, change entities, or make business decisions in endpoint delegates. Route/authorization metadata and HTTP status selection remain endpoint responsibilities.
- **SLICE-03:** Commands change tracked entities and persist through `SaveChangesAsync`. Queries do not mutate state or call save methods; they use `AsNoTracking()` and project required columns directly into query-owned response models.
- **SLICE-04:** EF entity instances never cross the HTTP response boundary, including entities nested in DTOs or collections. Returning a DTO that wraps an entity does not satisfy this rule.
- **SLICE-05:** Pass cancellation tokens to asynchronous persistence and dispatch operations. List operations must be bounded; the campaign example clamps page size to `1..100`.
- **SLICE-06:** Keep validation in the FluentValidation pipeline and request timing/logging in generic behaviors. Business invariants involving persisted state still need handler-level decisions and appropriate database constraints; input validators are not a substitute for concurrency-safe enforcement.

Do not add generic repositories, a custom unit of work, domain events, separate read/write databases, or a generic result abstraction by default. Revisit these choices only for a concrete requirement, recorded in an ADR.

## 4. Persistence, audit, and deletion

- Keep schema configuration and migrations under `Persistence`. Apply migrations explicitly; never migrate automatically at application startup.
- Auditing is opt-in through `IAuditableEntity`. Use the interceptor, not ad hoc handler audit writes. Business changes and audit rows must commit together.
- Exclude secrets, tokens, oversized values, and unnecessary personal data with `[AuditIgnore]`. Do not make `AuditLog` auditable or soft-deletable.
- Auditable keys must be available before saving. Follow [ADR-0006](adr/0006-use-sequential-guid-keys.md): leave new entity IDs unset and let EF generate the SQL Server sequential GUID when adding the entity. Do not introduce explicit random GUIDs or database-generated keys without revisiting the audit/key strategy. An ID is never a replacement for authorization.
- Soft delete is opt-in through `ISoftDeletable`. Its interceptor implements the storage mechanism; feature code decides whether deletion is permitted.
- Review every `IgnoreQueryFilters()` use for intent and authorization. It is an exceptional read path, not a convenient fix for a missing record.
- Follow the active-row unique-index convention for soft-deletable entities. Preserve the audit action `Deleted` even though the database operation becomes an update, including on retries.
- Bulk operations such as `ExecuteUpdate`, `ExecuteDelete`, and raw SQL can bypass change tracking and `SaveChanges` interceptors. Do not use them for audited or soft-deletable data without an explicitly reviewed equivalent strategy and tests.

See [ADR-0002](adr/0002-persist-audit-logs-with-ef-core-interceptor.md) and [ADR-0005](adr/0005-soft-delete-opt-in-entities.md).

## 5. Time, localization, security, and operations

- Inject `TimeProvider` and call `GetUtcNow()` for the current instant. Do not bypass injection by calling `TimeProvider.System` in a handler; its production registration belongs in composition code. Direct clock APIs listed in `BannedSymbols.txt` are build errors.
- Use UTC `DateTimeOffset` values in contracts and persistence. Reject nonzero offsets rather than silently normalizing them; clients should send the documented `+00:00` form. A validator checking `Offset == TimeSpan.Zero` verifies the parsed offset, not the exact original JSON spelling. Exact wire-format restrictions require separate parsing/contract tests.
- Keep user-local time formatting in clients unless a documented scheduling requirement changes this boundary.
- Put client-facing validation text and Problem Details titles in English/Turkish resources. Select culture only through `Accept-Language`; support the current specific and neutral tags with English fallback. Keep JSON keys, error keys, status/type/trace identifiers, logs, and persisted data culture-neutral.
- Require explicit authorization decisions for new business endpoints. Use centralized policy constants rather than inline role strings. Adding resource ownership or tenant checks requires feature-level design and tests; a role check alone does not establish ownership.
- Keep JWT validation in infrastructure; the starter does not issue tokens or manage identities. Keep secrets outside source control.
- Return validation/unexpected errors through the shared Problem Details handling. Do not expose exception details in unexpected-error responses or log request payloads, credentials, or bearer tokens.
- Keep health probes anonymous and non-sensitive. OpenAPI is exposed in Development/Testing; Swagger UI only in Development; neither is exposed in Production. Do not introduce a duplicate document generator.

See [ADR-0003](adr/0003-use-time-provider-and-utc-timestamps.md) and [ADR-0004](adr/0004-localize-api-errors.md).

## 6. How to detect violations

### Automated structural checks to add

These checks can live in `tests/DotNetBackendTemplate.UnitTests/Architecture` without adding a production project:

1. Verify handler and feature-validator ownership, including non-public and nested types such as `CreateCampaign.Handler`.
2. Inspect type/member dependencies for DEP-01 through DEP-06. Simple reflection is sufficient for discovering handler interfaces, but not every dependency inside method bodies; use suitable assembly or semantic source analysis for that scope. DEP-04 also requires responsibility review.
3. Compare source paths with declared namespaces for DEP-07. Assembly namespace checks alone cannot see the source folder.
4. Inspect endpoint contracts for entity leakage, including nested properties and collection element types. Retain HTTP serialization tests because untyped `IResult` responses can hide the concrete payload from signature-only checks.

If checks are introduced, prove each rule by temporarily adding a violating example and verifying that the relevant check fails. Include valid composition exceptions so the checks do not force architectural changes accidentally.

### Runtime checks

Use xUnit and Fluent Assertions for focused tests; use `WebApplicationFactory` with real SQL Server migrations and Testcontainers for persistence/API behavior. Relevant cases include validation, authorization, DTO projection, bounded pagination, audit transaction behavior, soft delete, injected time, localization, and documentation exposure.

Runtime tests demonstrate behavior, not ownership. A perfectly tested campaign workflow can still be misplaced under `Infrastructure`.

### Responsibility review: what automation cannot prove

An infrastructure class might implement `CanPublish(int count) => count < 5` without referencing any feature type. All namespace dependency checks could pass, but the code still contains a product policy.

For every new or changed class, ask:

- Does it decide eligibility, limits, pricing, state transitions, ownership, or workflow order? If yes, identify the owning feature.
- If the database, identity provider, or email vendor changed, would this rule still exist? If yes, that is a strong signal of feature responsibility, not proof on its own.
- Does an interceptor or generic behavior branch on a specific campaign command/status? Review it as a likely responsibility leak.
- Is this genuinely shared technical code, or feature code moved to `Common`/`Infrastructure` to reduce file size?

Business vocabulary is a review signal, not an automatic failure: persistence entities, resource keys, and accepted centralized policy configuration legitimately contain it.

## 7. Pull-request checklist

- [ ] Identify the owning feature or technical capability for every added class.
- [ ] Check dependency direction, source/namespace alignment, and the explicit exceptions above.
- [ ] Keep endpoints thin; keep handlers, validators, and DTOs in their owning slices.
- [ ] Verify commands/queries follow tracking, projection, cancellation, and pagination rules.
- [ ] Check entity leakage, authorization, secret handling, and request logging.
- [ ] Review schema changes, audit-sensitive properties, deletion paths, and interceptor bypasses.
- [ ] Use injected UTC time and resource-based client-facing messages.
- [ ] Add or update focused unit and integration tests for changed behavior.
- [ ] Update OpenAPI metadata and documentation when public behavior changes.
- [ ] Record boundary changes or justified exceptions in an ADR; do not silently weaken a check.

Run from the repository root; integration tests require Docker:

```bash
dotnet restore DotNetBackendTemplate.slnx
dotnet build DotNetBackendTemplate.slnx --no-restore -warnaserror
dotnet format DotNetBackendTemplate.slnx --verify-no-changes --no-restore
dotnet test DotNetBackendTemplate.slnx --no-build
```

CI/CD remains project-specific. These commands can run locally; this document does not require a GitHub Actions workflow.

## 8. Changing a rule

Document the requirement, affected rule IDs or sections, exact exception scope, alternatives, owner, and tests in an ADR. State when a temporary exception should be reviewed or removed. Update this document and conflicting guidance in the same change.

Architecture tests are guardrails, not a score proving that every responsibility is correctly placed. Combine structural checks, runtime tests, and responsibility review.
