# Architecture Rules

## Purpose and scope

This document is the review contract for this starter: one deployable ASP.NET Core API, vertical slices, logical CQRS, and one SQL Server database. It translates the [architecture overview](basic-architecture.md) and [accepted ADRs](adr/README.md) into placement and dependency rules.

These are conventions inside one assembly, not compiler-enforced project boundaries. Do not introduce separate Domain/Application/Infrastructure projects just to satisfy a generic Clean Architecture diagram. Direct `AppDbContext` injection into handlers is intentional.

Rules apply to production code under `src/DotNetBackendTemplate.Api`. Test fixtures may reference production areas and replace infrastructure. Generated migrations remain subject to schema review but are excluded from hand-written-code placement checks.

**Enforcement status:** rules marked *Test* in the [enforcement matrix](#enforcement-matrix) fail `dotnet test` when violated, and every failure names the rule it broke. Rules marked *Planned* are candidates for method-level checks, and rules marked *Review* cannot be proven structurally. Passing the tests therefore does not prove full architectural compliance; see [what automation cannot prove](#responsibility-review-what-automation-cannot-prove).

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
- **SLICE-03:** Commands change tracked entities and persist through `SaveChangesAsync`. Queries do not mutate state or call save methods; they use `AsNoTracking()` and project required columns directly into query-owned response models. Name the MediatR request `Command` when it changes state and `Query` when it only reads; automated CQRS checks identify queries by that name.
- **SLICE-04:** No type declared under `Persistence` — entity, enum, or anything else — crosses the HTTP boundary in either direction, including types nested in DTOs, collections, or generic results. Returning a DTO that wraps an entity does not satisfy this rule, and binding an entity from a request allows mass assignment. Expose API-owned contract types and map to them in the handler. `Features/Campaigns/CampaignStatus.cs` shows the pattern for an enum; it keeps the persistence enum's name so the JSON contract is unchanged.
- **SLICE-05:** Pass cancellation tokens to asynchronous persistence and dispatch operations. List operations must be bounded; the campaign example clamps page size to `1..100`.
- **SLICE-06:** Keep validation in the FluentValidation pipeline and request timing/logging in generic behaviors. Business invariants involving persisted state still need handler-level decisions and appropriate database constraints; input validators are not a substitute for concurrency-safe enforcement.
- **SLICE-07:** Every business endpoint under `/api` declares a stable name (`WithName`), a tag, a summary, a description, and response metadata, so the OpenAPI document describes it.

Do not add generic repositories, a custom unit of work, domain events, separate read/write databases, or a generic result abstraction by default. Revisit these choices only for a concrete requirement, recorded in an ADR.

## 4. Persistence, audit, and deletion

- **PERS-01:** Keep schema configuration and migrations under `Persistence`. Apply migrations explicitly; never migrate automatically at application startup.
- **PERS-02:** Auditing is opt-in through `IAuditableEntity`. Use the interceptor, not ad hoc handler audit writes. Business changes and audit rows must commit together.
- **PERS-03:** Exclude secrets, tokens, oversized values, and unnecessary personal data with `[AuditIgnore]`.
- **PERS-04:** Do not make `AuditLog` auditable or soft-deletable.
- **PERS-05:** Auditable keys must be available before saving. Follow [ADR-0006](adr/0006-use-sequential-guid-keys.md): leave new entity IDs unset and let EF generate the SQL Server sequential GUID when adding the entity. Do not introduce explicit random GUIDs or database-generated keys without revisiting the audit/key strategy. An ID is never a replacement for authorization.
- **PERS-06:** Soft delete is opt-in through `ISoftDeletable`, and every soft-deletable entity has the global query filter. Its interceptor implements the storage mechanism; feature code decides whether deletion is permitted.
- **PERS-07:** Review every `IgnoreQueryFilters()` use for intent and authorization. It is an exceptional read path, not a convenient fix for a missing record.
- **PERS-08:** Follow the active-row unique-index convention for soft-deletable entities: every unique index is filtered to rows that are not deleted.
- **PERS-09:** Preserve the audit action `Deleted` even though the database operation becomes an update, including on retries.
- **PERS-10:** Bulk operations such as `ExecuteUpdate`, `ExecuteDelete`, and raw SQL can bypass change tracking and `SaveChanges` interceptors. Do not use them for audited or soft-deletable data without an explicitly reviewed equivalent strategy and tests.
- **PERS-11:** Every entity type the DbContext maps is declared under `Persistence`.

See [ADR-0002](adr/0002-persist-audit-logs-with-ef-core-interceptor.md) and [ADR-0005](adr/0005-soft-delete-opt-in-entities.md).

## 5. Time, localization, security, and operations

- **TIME-01:** Inject `TimeProvider` and call `GetUtcNow()` for the current instant. Do not bypass injection by calling `TimeProvider.System` in a handler; its production registration belongs in composition code. Direct clock APIs listed in `BannedSymbols.txt` are build errors.
- **TIME-02:** Use UTC `DateTimeOffset` values in contracts and persistence. Reject nonzero offsets rather than silently normalizing them; clients should send the documented `+00:00` form. A validator checking `Offset == TimeSpan.Zero` verifies the parsed offset, not the exact original JSON spelling. Exact wire-format restrictions require separate parsing/contract tests.
- **TIME-03:** Keep user-local time formatting in clients unless a documented scheduling requirement changes this boundary.
- **LOC-01:** Put client-facing validation text and Problem Details titles in resources, with an entry in every supported language (currently English and Turkish). Select culture only through `Accept-Language`; support the current specific and neutral tags with English fallback. Keep JSON keys, error keys, status/type/trace identifiers, logs, and persisted data culture-neutral.
- **LOC-02:** Every resource key used in code must exist. `IStringLocalizer` does not fail on an unknown key; it returns the key name, so a typo reaches the client as text.
- **SEC-01:** Every endpoint makes an explicit authorization decision with `RequireAuthorization` or `AllowAnonymous`. Minimal API endpoints are anonymous by default, so a forgotten decision means a public endpoint.
- **SEC-02:** Use centralized policy constants rather than inline role strings.
- **SEC-03:** Adding resource ownership or tenant checks requires feature-level design and tests; a role check alone does not establish ownership.
- **SEC-04:** Keep JWT validation in infrastructure; the starter does not issue tokens or manage identities. Keep secrets outside source control.
- **SEC-05:** Return validation/unexpected errors through the shared Problem Details handling. Do not expose exception details in unexpected-error responses or log request payloads, credentials, or bearer tokens.
- **OPS-01:** Keep health probes anonymous and non-sensitive. OpenAPI is exposed in Development/Testing; Swagger UI only in Development; neither is exposed in Production. Do not introduce a duplicate document generator.

See [ADR-0003](adr/0003-use-time-provider-and-utc-timestamps.md) and [ADR-0004](adr/0004-localize-api-errors.md).

## 6. How to detect violations

### Enforcement matrix

| Rule | Enforcement | Where |
| --- | --- | --- |
| DEP-01, DEP-02, DEP-03, DEP-05, DEP-06 | Test | `DependencyRulesTests` |
| DEP-04 | Review — it is an allowance | — |
| DEP-07 | Test | `NamespaceRulesTests` |
| SLICE-01 | Test | `SliceRulesTests` |
| SLICE-02 | Test for database access in endpoint signatures; review for other logic | `EndpointRulesTests` |
| SLICE-03 | Planned: queries must not save and must use `AsNoTracking()` | — |
| SLICE-04, SLICE-07 | Test | `EndpointRulesTests` |
| SLICE-05 | Review; the campaign page-size clamp has runtime tests | `CampaignEndpointsTests` |
| SLICE-06 | Review | — |
| PERS-01 | Planned: no `Migrate` call in production code | — |
| PERS-02, PERS-09 | Runtime tests | `CampaignEndpointsTests` |
| PERS-03 | Review — only a person can judge what is sensitive | — |
| PERS-04, PERS-06, PERS-08, PERS-11 | Test | `PersistenceModelRulesTests` |
| PERS-05 | Test against the EF model; planned: no `Guid.NewGuid()` in Features or Persistence | `PersistenceModelRulesTests` |
| PERS-07 | Planned: an `IgnoreQueryFilters()` allowlist; intent remains a review | — |
| PERS-10 | Planned: no bulk operations or `ExecuteSql*` | — |
| TIME-01 | Analyzer (`BannedSymbols.txt`); planned: `TimeProvider.System` only in composition | — |
| TIME-02 | Runtime tests | `CreateCampaignValidatorTests`, `CampaignEndpointsTests` |
| TIME-03, SEC-03, SEC-04 | Review | — |
| LOC-01 | Test for completeness; runtime tests for culture selection | `LocalizationRulesTests`, `CampaignEndpointsTests` |
| LOC-02, SEC-01 | Test | `LocalizationRulesTests`, `EndpointRulesTests` |
| SEC-02 | Planned: `RequireRole` only in Infrastructure | — |
| SEC-05 | Runtime tests for the Problem Details shape; log content is a review | `CampaignEndpointsTests` |
| OPS-01 | Runtime tests | `SwaggerUiTests`, `CampaignEndpointsTests` |

Section 1 responsibility placement is a review throughout.

### Architecture tests

The tests live in `tests/DotNetBackendTemplate.UnitTests/Architecture` and need neither Docker nor a database. Each rule is a check that runs twice: against the production code, where it must report nothing, and against a miniature application under `Architecture/Fixtures/Violations` that breaks the rule on purpose, where it must report the violation. A check that stops catching its deliberate violation has silently stopped working, and that test fails too.

Dependency rules read compiled IL through ArchUnitNET, so a forbidden reference hidden in an async method, a lambda, or a generic argument is still found; searching `using` directives would miss all three. Endpoint rules inspect the endpoints the running host actually maps. Persistence rules read the EF design-time model. Names are derived from the assembly rather than hard-coded, so the tests survive the clone-and-rename checklist.

A failure lists every violation with its rule ID, the offending type or endpoint, and what to do:

```text
[SLICE-04] GET /api/v1/campaigns/{id:guid}: returns DotNetBackendTemplate.Api.Persistence.Entities.CampaignStatus. Responses must use API-owned types; map persistence types in the handler.
```

Fix the code, or record a justified exception in an ADR as described in section 8 before changing a check. Retain HTTP serialization tests as well: an endpoint returning an untyped `IResult` without `Produces` metadata hides its payload from signature-based checks.

### Planned method-level checks

These need method-call analysis and are the next step: SLICE-03, PERS-01, the code side of PERS-05, PERS-07, PERS-10, TIME-01, and SEC-02 in the matrix above.

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

When a rule changes, update its check and its deliberate violation under `Architecture/Fixtures/Violations` in the same change. A new rule needs both. A check whose fixture no longer violates it proves nothing.

Architecture tests are guardrails, not a score proving that every responsibility is correctly placed. Combine structural checks, runtime tests, and responsibility review.
