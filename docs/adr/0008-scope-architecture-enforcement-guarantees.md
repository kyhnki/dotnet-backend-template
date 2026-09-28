# ADR-0008: Scope architecture enforcement guarantees

- Status: Accepted
- Date: 2026-09-29
- Decision owners: Backend team

## Context

Architecture checks protect this single-project starter, but some structural observations were described as stronger guarantees: a strategy creation call does not prove transaction scope, any query filter does not guarantee soft deletion, and a no-tracking call does not cover every query path. Overly broad checks also reject valid split-slice layouts and non-key GUID uses.

## Decision

Separate automated scoped checks, partial checks requiring review, runtime scenarios, and responsibility review in the architecture rules matrix. Do not introduce a general-purpose semantic analyzer or new production layers.

Require mandatory active-row predicates for soft-delete filters and the normalized active-row unique-index predicate. Probe slice mappings independently and compare emitted handler/HTTP-method identities, not the owning class. This still requires review of route prefixes and group policies.

Gate explicit transactions, multiple save call sites and non-key GUID generation with exact-method exceptions carrying a rationale, ADR and behavior-test reference. Creating an execution strategy is not an exemption. Method-call analysis is not control-flow analysis; loops and indirect calls remain review concerns. No production exception is introduced by this ADR.

Add real SQL rollback/filtered-uniqueness tests, controlled audit re-entry tests, and real bearer validation with local signing/discovery configuration. Keep current HTTP contracts, authentication behavior and production migrations unchanged.

Clarify that persisted enum strings make member names part of the stored contract; renaming requires a migration or compatible mapping. Document/test ID checks prove inventory consistency only. Localization regex checks remain explicitly partial.

## Consequences

- Positive and negative regression examples protect both detection and valid code arrangements.
- A green suite is not a proof of complete transaction safety or correct responsibility placement.
- Unrecognized custom filters need a specific reviewed extension, not a broad exemption.
- SQL retry tests exercise re-entered SaveChanges, not every possible provider retry or commit ambiguity.
- No extra production abstraction, dependency, schema or CI workflow is introduced.

## Alternatives considered

- A full Roslyn/control-flow framework was deferred because its complexity is disproportionate to this starter.
- Broad type/namespace allowlists and unrelated strategy-call exemptions were rejected because they silently weaken boundaries.
