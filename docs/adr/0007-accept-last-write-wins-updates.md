# ADR-0007: Accept last-write-wins updates for the MVP

- Status: Accepted
- Date: 2026-09-29
- Decision owners: Backend team

## Context

`UpdateCampaign` loads the tracked entity, changes it, and saves. No entity has a concurrency token, so when two clients update the same campaign at the same time, the second save silently overwrites the first. Nothing fails and neither client is told.

Optimistic concurrency is the usual answer, but it is not free: it changes the public API. Clients must receive a version, send it back on every update and delete, and handle a new failure response when their version is stale. The template has no concurrent-editing requirement yet, and each generated or hand-written client would carry that cost from day one.

The audit trail changes the risk. Every update of an auditable entity records the old and new values of the changed properties in the same transaction (ADR-0002), so an overwritten value is not lost from history: it can be seen and restored from the audit log.

## Decision

Accept last-write-wins updates for the MVP. Add no concurrency token to entities by default.

This is a deliberate default, not an oversight. Adopt optimistic concurrency for an entity as soon as any of these holds: users edit the same record concurrently in practice, an overwrite has business cost that the audit trail cannot repair, or a workflow depends on a read-then-write invariant.

When adopting it:

1. Add a `byte[] RowVersion` property configured with `IsRowVersion()`, and mark it `[AuditIgnore]`, since it is an opaque database-generated value. Add the migration in the same change (PERS-12).
2. Return the version as an `ETag` header from the read endpoint, and require `If-Match` on update and delete.
3. Map `DbUpdateConcurrencyException` to `412 Precondition Failed` Problem Details with a localized title, through the shared exception handling, and document the response in OpenAPI.
4. Add integration tests for the stale-version path.

`DbUpdateConcurrencyException` is not a transient failure, so `EnableRetryOnFailure` does not retry it. A soft delete is an update and bumps the row version like any other.

## Consequences

- The API contract stays simple: no versions, no `If-Match`, no conflict responses.
- Concurrent updates to the same row lose the earlier write in the current state, without an error. The audit trail keeps both writes, so the loss is visible and recoverable, but only after the fact.
- The trigger conditions above make the upgrade a planned change rather than a surprise, and the steps make it mechanical.
- Adding concurrency later is a breaking change for clients of the affected endpoints and must be versioned or coordinated.

## Alternatives considered

- **Row versions on every entity now** was rejected for the MVP: it forces the contract cost onto every client before any requirement justifies it.
- **An application-managed integer version** has the same contract cost as a row version and is easier to get wrong.
- **Pessimistic locking** was rejected: it holds database locks across user think-time and does not fit a stateless HTTP API.
