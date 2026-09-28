# Architecture Rules

## Purpose and scope

This document is the review contract for this starter: one deployable ASP.NET Core API, vertical slices, logical CQRS, and one SQL Server database. It translates the [architecture overview](basic-architecture.md) and [accepted ADRs](adr/README.md) into placement and dependency rules.

These are conventions inside one assembly, not compiler-enforced project boundaries. Do not introduce separate Domain/Application/Infrastructure projects just to satisfy a generic Clean Architecture diagram. Direct `AppDbContext` injection into handlers is intentional.

Rules apply to production code under `src/DotNetBackendTemplate.Api`. Test fixtures may reference production areas and replace infrastructure. Generated migrations remain subject to schema review but are excluded from hand-written-code placement checks.

**Enforcement status:** the [enforcement matrix](#enforcement-matrix) separates *Test* (automated within the stated scope), *Partial test + review* (heuristics or incomplete structural coverage), *Analyzer*, *Runtime tests* (the exercised scenarios), and *Review*. A passing partial check is not proof of the whole rule. Failures identify the rule; passing all checks does not prove full architectural compliance. See [what automation cannot prove](#responsibility-review-what-automation-cannot-prove).

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
| DEP-08 | Request handlers must not depend on `ISender`, `IMediator`, or `IPublisher`. Dispatching another request from a handler nests pipelines and hides the cross-feature coordination DEP-05 reserves for design review. | Dependency analysis |

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
- **SLICE-06:** Keep validation in the FluentValidation pipeline and request timing/logging in generic behaviors. Business invariants involving persisted state still need handler-level decisions and appropriate database constraints; input validators are not a substitute for concurrency-safe enforcement. Updates are last-write-wins by decision until an entity adopts optimistic concurrency ([ADR-0007](adr/0007-accept-last-write-wins-updates.md)).
- **SLICE-07:** Every business endpoint under `/api` declares a stable name (`WithName`), a tag, a summary, a description, and response metadata, so the OpenAPI document describes it. Each contract type gets its own schema, and the documented schema must match the type the endpoint binds or returns.
- **SLICE-08:** Every slice that defines public static `MapEndpoint(RouteGroupBuilder)` is registered in `EndpointRouteBuilderExtensions`. Mapping must only declare routes, not perform I/O or business work: tests invoke it in an isolated builder and compare the emitted handler/method identities with the real host. HTTP delegates may be separate top-level types. This does not prove route-prefix or authorization-group correctness; runtime contract tests and review still cover those.

Do not add generic repositories, a custom unit of work, domain events, separate read/write databases, or a generic result abstraction by default. Revisit these choices only for a concrete requirement, recorded in an ADR.

## 4. Persistence, audit, and deletion

- **PERS-01:** Keep schema configuration and migrations under `Persistence`. Apply migrations explicitly; never migrate automatically at application startup.
- **PERS-02:** Auditing is opt-in through `IAuditableEntity`. Use the interceptor, not ad hoc handler audit writes. Business changes and audit rows must commit together.
- **PERS-03:** Exclude secrets, tokens, oversized values, and unnecessary personal data with `[AuditIgnore]`. A property on an auditable entity whose name looks sensitive (password, secret, token, API key, private key, connection string) must carry it; the name check is a heuristic that prompts the decision, and only a person can judge what else is sensitive.
- **PERS-04:** Do not make `AuditLog` auditable or soft-deletable.
- **PERS-05:** Auditable keys must be available before saving. Follow [ADR-0006](adr/0006-use-sequential-guid-keys.md): leave new entity IDs unset and let EF generate the SQL Server sequential GUID when adding the entity. Do not introduce explicit random GUIDs or database-generated keys without revisiting the audit/key strategy. The call check cannot infer what a GUID is used for: non-key GUID generation is allowed through a reviewed, exact-method exception with rationale, ADR, and behavior-test reference. An ID is never a replacement for authorization.
- **PERS-06:** Soft delete is opt-in through `ISoftDeletable`, and every soft-deletable entity has a mandatory active-row filter. The checker recognizes `!row.IsDeleted` or `row.IsDeleted == false`, including mandatory `&&` clauses on the filter parameter. Unrelated, optional (`||`), reversed, and unrecognized expressions fail and need review, not a silent exemption. The interceptor implements storage; the feature decides whether deletion is permitted.
- **PERS-07:** Review every `IgnoreQueryFilters()` use for intent and authorization. It is an exceptional read path, not a convenient fix for a missing record. A reviewed use is added, with its reason, to the allowlist in `MethodCallRulesTests`.
- **PERS-08:** Every unique index on a soft-deletable entity uses `[IsDeleted] = 0`. Whitespace and parentheses are normalized; reversed and custom predicates fail for review. Merely containing the word `IsDeleted` is insufficient. Runtime tests verify both active duplicate rejection and value reuse after soft delete.
- **PERS-09:** Preserve the audit action `Deleted` even though the database operation becomes an update, including on retries.
- **PERS-10:** Bulk operations such as `ExecuteUpdate`, `ExecuteDelete`, and raw SQL can bypass change tracking and `SaveChanges` interceptors. Do not use them for audited or soft-deletable data without an explicitly reviewed equivalent strategy and tests.
- **PERS-11:** Every entity type the DbContext maps is declared under `Persistence`.
- **PERS-12:** The model always matches the latest migration. Add the migration in the same change as the entity or configuration change; since migrations are never applied automatically, a missing one would first surface in production.
- **PERS-13:** Store enums as strings with `HasConversion<string>()` to avoid dependence on numeric ordering. Enum names become persisted values: renaming or removing a member requires a data migration or backward-compatible mapping. String conversion does not make renames safe.
- **PERS-14:** Give every string column a maximum length, or an explicit `HasColumnType("nvarchar(max)")` when unbounded is intended. An implicit `nvarchar(max)` cannot be indexed.
- **PERS-15:** Do not cascade-delete from a soft-deletable principal to a dependent that is not soft-deletable: soft-deleting the parent would hard-delete the child.
- **PERS-16:** A dependent with a required relationship to a soft-deletable, filtered principal needs a query filter too. EF Core reports the mismatch only as a runtime log warning, which `TreatWarningsAsErrors` does not see.
- **PERS-17:** Auditable entities do not use complex or owned types until the audit interceptor reads them; their values would otherwise be missing from the audit trail.
- **PERS-18:** Explicit transactions must execute inside the execution strategy's delegate. Creating a strategy without executing the transaction inside it proves nothing. Direct transaction calls require a reviewed exact-method exception with rationale, ADR, and rollback-test reference.
- **PERS-19:** A command normally saves once. Multiple saves require an explicitly reviewed shared transaction executed through the execution strategy, plus an exact-method exception and atomicity test. IL call-site counts are not runtime counts: loops, conditional branches, and helper methods remain a review. Strategy creation never exempts a caller automatically.

See [ADR-0002](adr/0002-persist-audit-logs-with-ef-core-interceptor.md) and [ADR-0005](adr/0005-soft-delete-opt-in-entities.md).

## 5. Time, localization, security, and operations

- **TIME-01:** Inject `TimeProvider` and call `GetUtcNow()` for the current instant. Do not bypass injection by calling `TimeProvider.System` in a handler; its production registration belongs in composition code. Direct clock APIs listed in `BannedSymbols.txt` are build errors.
- **TIME-02:** Use UTC `DateTimeOffset` values in contracts and persistence. Reject nonzero offsets rather than silently normalizing them; clients should send the documented `+00:00` form. A validator checking `Offset == TimeSpan.Zero` verifies the parsed offset, not the exact original JSON spelling. Exact wire-format restrictions require separate parsing/contract tests. Entities and HTTP contracts never use `DateTime`.
- **TIME-03:** Keep user-local time formatting in clients unless a documented scheduling requirement changes this boundary.
- **LOC-01:** Put client-facing validation text and Problem Details titles in resources, with an entry in every supported language (currently English and Turkish). Select culture only through `Accept-Language`; support the current specific and neutral tags with English fallback. Keep JSON keys, error keys, status/type/trace identifiers, logs, and persisted data culture-neutral.
- **LOC-02:** Every resource key used in code must exist. `IStringLocalizer` does not fail on an unknown key; it returns the key name, so a typo reaches the client as text.
- **LOC-03:** Validators take their messages from the localizer. A string literal passed to `WithMessage` bypasses the resources and reaches every client in English.
- **SEC-01:** Every endpoint makes an explicit authorization decision with `RequireAuthorization` or `AllowAnonymous`. Minimal API endpoints are anonymous by default, so a forgotten decision means a public endpoint.
- **SEC-02:** Use centralized policy constants rather than inline role strings: endpoints never pass role names directly, and only Infrastructure builds role requirements.
- **SEC-03:** Adding resource ownership or tenant checks requires feature-level design and tests; a role check alone does not establish ownership.
- **SEC-04:** Keep JWT validation in infrastructure; the starter does not issue tokens or manage identities. Keep secrets outside source control.
- **SEC-05:** Return validation/unexpected errors through the shared Problem Details handling. Status-code-only responses (401, 403, 404) are Problem Details with a localized title as well, through `UseStatusCodePages`. Do not expose exception details in unexpected-error responses or log request payloads, credentials, or bearer tokens.
- **SEC-06:** Every policy an endpoint references is registered. A misspelled policy name compiles, then throws on the first request that needs it.
- **OPS-01:** Keep health probes anonymous and non-sensitive. OpenAPI is exposed in Development/Testing; Swagger UI only in Development; neither is exposed in Production. Do not introduce a duplicate document generator.
- **OPS-02:** Keep MediatR below 13, FluentAssertions below 8, and AutoMapper below 15; their next major versions require a commercial license. Review the license and record an ADR before raising a limit.

See [ADR-0003](adr/0003-use-time-provider-and-utc-timestamps.md) and [ADR-0004](adr/0004-localize-api-errors.md).

## 6. How to detect violations

### Enforcement matrix

| Rule | Enforcement | Where |
| --- | --- | --- |
| DEP-01, DEP-02, DEP-03, DEP-05, DEP-06, DEP-08 | Test | `DependencyRulesTests` |
| DEP-04 | Review — it is an allowance | — |
| DEP-07 | Test | `NamespaceRulesTests` |
| SLICE-01 | Test | `SliceRulesTests` |
| SLICE-02 | Test for database access in endpoint signatures; review for other logic | `EndpointRulesTests` |
| SLICE-03 | Partial test + review: request suffix, direct saves and presence of a no-tracking call; not all query paths, projections or helper side effects | `MethodCallRulesTests`, `EnforcementRegressionTests` |
| SLICE-04 | Test for reachable declared contract types; opaque/untyped results need serialization tests and review | `EndpointRulesTests` |
| SLICE-08 | Partial test + review: handler/HTTP-method identities; not route prefixes or group configuration | `EndpointRulesTests`, `EnforcementRegressionTests` |
| SLICE-05 | Analyzer (CA2016) for forwarding cancellation tokens; review for bounded lists, with runtime tests for the campaign page-size clamp | `CampaignEndpointsTests` |
| SLICE-06 | Review | — |
| SLICE-07 | Test for metadata; runtime test that each operation's schema matches its contract type | `EndpointRulesTests`, `OpenApiContractTests` |
| PERS-01, PERS-07, PERS-10 | Partial test + review: recognized direct EF calls | `MethodCallRulesTests` |
| PERS-18, PERS-19 | Partial test + review: exact-method exception gate; transaction scope and runtime save counts are not proven | `MethodCallRulesTests`, `EnforcementRegressionTests` |
| PERS-02, PERS-09 | Runtime tests: SQL failure rollback and controlled re-entry of SaveChanges; not every provider retry/commit-ambiguity scenario | `CampaignEndpointsTests`, `PersistenceSafetyTests` |
| PERS-03 | Test for sensitive-looking names (heuristic); what else is sensitive remains a review | `PersistenceModelRulesTests` |
| PERS-04, PERS-06, PERS-08, PERS-11, PERS-12, PERS-13, PERS-14, PERS-15, PERS-16, PERS-17 | Test | `PersistenceModelRulesTests` |
| PERS-05 | Partial test + review: EF key metadata and GUID calls, with exact-method exceptions for non-key uses | `PersistenceModelRulesTests`, `MethodCallRulesTests`, `EnforcementRegressionTests` |
| TIME-01 | Test for `TimeProvider.System` outside composition; analyzer (`BannedSymbols.txt`) for the clock APIs | `MethodCallRulesTests` |
| TIME-02 | Test for `DateTime` in entities and contracts; runtime tests for offsets | `PersistenceModelRulesTests`, `EndpointRulesTests`, `CreateCampaignValidatorTests`, `CampaignEndpointsTests` |
| TIME-03, SEC-03 | Review | — |
| SEC-04 | Runtime tests for real JWT issuer, audience, signature, expiry, missing token and role enforcement; secrets and identity ownership remain review | `JwtAuthenticationTests` |
| LOC-01 | Test for completeness; runtime tests for culture selection | `LocalizationRulesTests`, `CampaignEndpointsTests` |
| LOC-02, LOC-03 | Partial test + review: literal localizer keys with the recognized variable-name pattern and direct literal WithMessage calls only | `LocalizationRulesTests`, `EnforcementRegressionTests` |
| SEC-01, SEC-06 | Test | `EndpointRulesTests` |
| SEC-02 | Test for inline roles and for `RequireRole` outside Infrastructure | `EndpointRulesTests`, `MethodCallRulesTests` |
| SEC-05 | Runtime tests for Problem Details on 400, 401, 403, and 404; log content is a review | `CampaignEndpointsTests`, `OpenApiContractTests` |
| OPS-01 | Runtime tests | `SwaggerUiTests`, `CampaignEndpointsTests` |
| OPS-02 | Test | `PackageRulesTests` |
| DOC-01 | Test for document/test inventory consistency only | `ArchitectureRulesDocumentTests` |

Section 1 responsibility placement is a review throughout.

### Architecture tests

The tests live in `tests/DotNetBackendTemplate.UnitTests/Architecture` and need neither Docker nor a database. Checks run against production code and deliberate violations (fixture types, model expressions or source snippets). Positive examples also protect valid arrangements, such as split slices and non-key GUID exceptions. Negative examples establish that the specific example is caught, not that every possible violation is detectable.

Dependency rules read compiled IL through ArchUnitNET, so a forbidden reference hidden in an async method, a lambda, or a generic argument is still found; searching `using` directives would miss all three. Method-call rules read call instructions through Mono.Cecil and attribute calls inside async state machines and lambdas to the type the developer wrote. Endpoint rules inspect the endpoints the running host actually maps. Persistence rules read the EF design-time model. Names are derived from the assembly rather than hard-coded, so the tests survive the clone-and-rename checklist.

A failure lists every violation with its rule ID, the offending type or endpoint, and what to do:

```text
[SLICE-04] GET /api/v1/campaigns/{id:guid}: returns DotNetBackendTemplate.Api.Persistence.Entities.CampaignStatus. Responses must use API-owned types; map persistence types in the handler.
```

Fix the code, or record a justified exception in an ADR as described in section 8 before changing a check. Retain HTTP serialization tests as well: an endpoint returning an untyped `IResult` without `Produces` metadata hides its payload from signature-based checks.

`ReviewedExceptions` in `MethodCallRulesTests` is initially empty. Each entry identifies one rule and a complete IL method signature (return type, declaring type, method name and parameter types), with rationale, ADR path and behavior-test name. Exemptions do not propagate to other overloads or helper methods. Async state-machine calls are mapped to their original method; closures retain their precise generated identity rather than inheriting broad exemptions. Reference checks establish inventory, not whether the cited test proves the exception safe.

Localization checks intentionally remain small heuristics: `localizer["Key"]` literal keys and direct literal `WithMessage` arguments are recognized. Alternative names such as `messages["Key"]`, variable keys/messages and lambda messages require review. Regression tests document these blind spots instead of pretending regex provides semantic analysis.

### Runtime checks

Use xUnit and Fluent Assertions for focused tests; use `WebApplicationFactory` with real SQL Server migrations and Testcontainers for persistence/API behavior. Relevant cases include validation, authorization, DTO projection, bounded pagination, audit transaction behavior, soft delete, injected time, localization, and documentation exposure.

`PersistenceSafetyTests` uses a real SQL constraint failure to verify rollback from a fresh context, and a test-only retry strategy/fault after audit staging to verify exactly one audit record on re-entry for creation and soft deletion. This does not simulate every SQL Server transient failure or an ambiguous commit. A separate test-only table in the disposable container checks filtered uniqueness without altering production migrations. `JwtAuthenticationTests` uses the real bearer handler and ephemeral RSA keys with local discovery metadata; the existing test authentication scheme remains for fast campaign tests.

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

- **DOC-01:** The enforcement matrix inventories every defined rule. Its *Test* and *Partial test* rows must match rule-ID literals and class names present in test source. `ArchitectureRulesDocumentTests` detects textual inventory drift, not whether an assertion executes or semantically proves a rule. Runtime-test coverage is reviewed separately.

Architecture tests are guardrails, not a score proving that every responsibility is correctly placed. Combine structural checks, runtime tests, and responsibility review.
