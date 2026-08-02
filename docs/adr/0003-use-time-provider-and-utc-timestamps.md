# ADR-0003: Use TimeProvider and require UTC timestamps

- Status: Accepted
- Date: 2026-08-01
- Decision owners: Backend team

## Context

Features and infrastructure need the current instant for persisted timestamps. Calling static system-clock properties couples code to wall-clock time, makes tests nondeterministic, and can accidentally introduce server-local timestamps. A custom clock abstraction would duplicate functionality provided by modern .NET.

## Decision

Inject the built-in `TimeProvider` wherever code needs the current instant and call `GetUtcNow()`. Production registers `TimeProvider.System`; tests replace it with a controllable provider.

Use `DateTimeOffset` for API contracts and persisted timestamps. Timestamp inputs must contain the explicit UTC offset `+00:00`; the API rejects non-UTC offsets rather than silently changing their meaning. SQL Server stores these values as `datetimeoffset`.

Apply the banned-API analyzer to every project and reject `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, and `DateTimeOffset.UtcNow`. Fixed timestamp construction remains valid in tests and migrations.

## Consequences

- Time-dependent behavior is deterministic in tests.
- Application and audit timestamps share one request-independent clock source.
- Server timezone configuration cannot change persisted timestamps.
- Clients must convert user-local values to UTC before sending them.
- User or organization timezone preferences and daylight-saving scheduling remain future domain features, not infrastructure defaults.

## Alternatives considered

- A custom `IDateTimeService` was rejected because `TimeProvider` already supplies the required abstraction.
- Accepting arbitrary offsets and normalizing them was rejected to keep the API contract explicit and prevent accidental local-time submission.
