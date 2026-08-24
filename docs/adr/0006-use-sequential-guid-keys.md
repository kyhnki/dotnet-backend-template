# ADR-0006: Use client-generated sequential GUID keys

- Status: Accepted
- Date: 2026-08-24
- Decision owners: Backend team

## Context

`int` identity keys are smaller, readable, and the obvious default for an MVP, so the choice of `Guid` needs a reason beyond preference.

The reason is ADR-0002. The audit interceptor reads an entity's key during `SavingChanges`, before the database has assigned anything, so an auditable entity must know its key before it is saved. An `int` IDENTITY column is still `0` at that moment, which would make every insert audit as `EntityId = "0"`. Supporting `int` would mean staging added entries and writing audit rows in `SavedChanges` inside an explicit transaction — more machinery than the current design, not less. Sequential integers also expose record counts and invite enumeration in a public API.

`Guid` has a genuine cost, though. EF Core makes the primary key clustered on SQL Server, and randomly generated v4 GUIDs scatter inserts through the B-tree, causing page splits and index fragmentation.

## Decision

Keep `Guid` keys and do not assign them in application code. Leave `Id` unset on new entities and let EF Core's `SequentialGuidValueGenerator` supply it, which happens **client-side when the entity is added** — before `SaveChanges`, so ADR-0002's requirement still holds.

Do not use `NEWSEQUENTIALID()` or any other database-generated default on auditable entities; that reintroduces the problem `int` has.

Do not use `Guid.CreateVersion7()` on SQL Server. UUIDv7 places its timestamp in the leading bytes, while SQL Server compares `uniqueidentifier` values starting from the last six bytes, so v7 values are not sequential in the order that actually governs the clustered index. UUIDv7 would be the right answer on PostgreSQL.

## Consequences

- Inserts append to the end of the clustered index instead of scattering, so the primary key stops fragmenting.
- Keys remain opaque, so IDs in URLs do not reveal record counts or support enumeration.
- Key generation stays client-side, so auditing, the `Created` location header, and any code reading the key immediately after `Add` keep working.
- The generation strategy is now EF Core's rather than the application's. Re-introducing an explicit `Id = Guid.NewGuid()` on an entity silently restores the fragmentation this decision removes.
- The ordering guarantee is provider-specific. Moving off SQL Server means revisiting this decision; on PostgreSQL, UUIDv7 becomes the correct choice.
- Keys are 16 bytes rather than 4, and that width is repeated in every nonclustered index. This is accepted for the audit and enumeration properties above.

## Alternatives considered

- `int` identity keys were rejected because they break the audit trail for inserts unless the interceptor is redesigned around `SavedChanges`, and because they expose enumerable IDs.
- Keeping `Guid.NewGuid()` was rejected once a no-cost alternative existed; it produced random values as a clustered key for no benefit.
- `Guid.CreateVersion7()` was rejected on SQL Server for the byte-ordering reason above.
- A non-clustered primary key with the clustered index moved to another column was rejected as more schema complexity than the problem warrants at this size.
