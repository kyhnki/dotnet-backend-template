# ADR-0005: Soft-delete opt-in entities with a global query filter

- Status: Accepted
- Date: 2026-08-23
- Decision owners: Backend team

## Context

Some business records should survive deletion so that mistakes can be recovered and history stays intelligible, while others have no reason to outlive their removal. The template already expresses a per-entity opt-in through `IAuditableEntity`, and reusing that shape avoids introducing a second, unfamiliar mechanism.

Audit logs alone do not solve this. They record that a delete happened and what the row contained, but the row itself is gone, so recovery means reconstructing an entity from JSON. Soft delete keeps the row addressable.

Soft delete also changes what a delete *is*: the database receives an update. Anything that reasons about deletes — the audit trail in particular — has to keep meaning what it meant before.

## Decision

Add `ISoftDeletable` with `IsDeleted` and `DeletedAtUtc`. Entities opt in by implementing it. Unlike `IAuditableEntity` it is not a bare marker, because the mechanism needs the state.

Apply the `!IsDeleted` global query filter by convention in `OnModelCreating` for every entity type implementing the interface, so opting in is the only step a feature author takes. Reads bypass the filter with `IgnoreQueryFilters()`.

Convert deletes in a dedicated `SoftDeleteSaveChangesInterceptor`: entries in `EntityState.Deleted` become `Modified` with `IsDeleted` set and `DeletedAtUtc` taken from the injected `TimeProvider` (ADR-0003). Re-converting an already-converted entry is a no-op so a retried `SaveChanges` is safe.

Derive the audit action from meaning rather than from `EntityState`. `AuditSaveChangesInterceptor` records `Deleted` when the entry is deleted **or** when an `ISoftDeletable` entry's `IsDeleted` moved from `false` to `true`. Do not depend on interceptor registration order.

Do not add `DeletedBy`. The audit row's `UserId` already answers who.

## Consequences

- A soft-deleted row disappears from every ordinary query, including the ones inside feature handlers, without those handlers knowing the feature exists.
- The audit trail keeps saying `Deleted` for a delete, so "who removed this campaign" stays answerable with one query.
- Deriving the action from `IsDeleted` rather than `EntityState` keeps the audit correct under `EnableRetryOnFailure`, where a retried save re-enters the pipeline with the entry already converted, and makes interceptor ordering irrelevant.
- Unique indexes on a soft-deletable entity must be filtered with `HasFilter("[IsDeleted] = 0")`, or a deleted row permanently reserves its unique value. Indexes serving normal reads should be filtered the same way to match the query filter.
- Deleted rows accumulate. Retention and purge policy become an operational requirement, not an optional one.
- Soft delete does not satisfy an erasure request. A real "delete my data" path needs a hard delete that bypasses this mechanism.
- Required relationships between filtered and unfiltered entities produce EF Core warnings; with warnings as errors, the interface must be applied consistently across a relationship.
- `AuditLog` must never implement `ISoftDeletable`; audit rows are immutable records, not business data.

## Alternatives considered

- Relying on audit logs alone was rejected because recovering a row from serialized JSON is not a recovery path anyone will trust under pressure.
- A `DeletedAt IS NULL` timestamp-only convention without a marker interface was rejected because the filter could not be applied automatically and each configuration would have to remember it.
- Registering the soft-delete interceptor after the audit interceptor so audit still observes `EntityState.Deleted` was rejected: it makes correctness depend on implicit registration order and silently produces the wrong action on a retried save.
- A `Restore` endpoint was deferred. Nothing in the template needs an undelete flow yet, and `IgnoreQueryFilters()` makes one straightforward to add.
