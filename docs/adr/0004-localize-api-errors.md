# ADR-0004: Localize API errors through Accept-Language

- Status: Accepted
- Date: 2026-08-01
- Decision owners: Backend team

## Context

API consumers need validation and general error text in supported languages, but localized machine fields or persisted values would make integrations unstable. Culture selection also needs one standard, predictable mechanism.

## Decision

Support English and Turkish, with `en-US` as the default and fallback culture. Accept both specific (`en-US`, `tr-TR`) and neutral (`en`, `tr`) language tags, because API clients commonly send the bare neutral tag. Store translations in neutral-culture resource files so specific tags resolve through parent-culture fallback. Resolve culture only from the standard `Accept-Language` request header and return the resolved value in `Content-Language`.

Store validation messages and Problem Details titles in shared resources. Keep JSON property names, validation error keys, status codes, Problem Details types, trace identifiers, route names, logs, OpenAPI metadata, and business data culture-neutral. JSON timestamps remain ISO 8601 UTC values and are never formatted for a user's locale by the API.

Unsupported or missing languages use English. Query-string, cookie, and custom-header culture providers are disabled.

## Consequences

- Clients can request translated error text without changing how they parse responses.
- Each new client-facing error requires English and Turkish resource entries.
- Successful data responses remain identical across cultures apart from the `Content-Language` response header.
- Adding a language requires resources, supported-culture registration, and localization tests.

## Alternatives considered

- Localizing API field names and persisted data was rejected because it would create language-dependent contracts.
- Query-string and cookie culture selection were rejected because API clients can use `Accept-Language` consistently.
- Localizing OpenAPI metadata was deferred because this starter treats its contract documentation as developer-facing and culture-neutral.
