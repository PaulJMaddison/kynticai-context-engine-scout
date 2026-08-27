# WP-025 — Rationalise REST, GraphQL and SDK compatibility surfaces

## Metadata

- **Status:** Planned
- **Priority:** Medium
- **Phase:** H — Maintainability and developer experience
- **Depends on:** WP-013, WP-014, WP-017
- **Review gate:** xhigh (public API/SDK compatibility)

## Context

Scout currently carries:

- legacy REST
- versioned `/api/v1` REST
- GraphQL
- TypeScript SDK
- .NET SDK

Multiple public surfaces are valuable only if customers use them. They also multiply auth, pagination, error, testing, documentation and compatibility obligations. Scout has already experienced a real SDK/API enum mismatch.

## Objective

Choose a deliberate long-term contract strategy rather than allowing every historical API surface to become permanent.

Recommended direction:

- `/api/v1` becomes the canonical stable machine contract;
- TypeScript/.NET SDKs wrap the canonical contract;
- GraphQL remains supported where it provides demonstrated value;
- legacy unversioned REST enters a documented deprecation path rather than receiving new capabilities forever.

This package is a design + compatibility implementation package, not permission to break existing clients immediately.

## Tasks

1. Inventory every endpoint and map equivalent capability across legacy REST, v1 REST, GraphQL and SDKs.
2. Identify capability gaps and inconsistent semantics: auth, scopes, pagination, errors, nullability, enum serialisation and tenant/workspace rules.
3. Publish an API support/deprecation policy.
4. Stop adding new features to a deprecated legacy surface.
5. Add deprecation headers/docs where technically appropriate.
6. Ensure SDKs target one canonical versioned surface.
7. Decide GraphQL support level based on actual product use; do not remove it merely for tidiness.
8. Add contract-parity tests for critical overlapping operations.
9. Define a future versioning process for breaking changes.

## Acceptance criteria

- [ ] One canonical machine API is named explicitly.
- [ ] SDK implementation aligns with that canonical API.
- [ ] Legacy API has a non-breaking deprecation policy.
- [ ] GraphQL's role is explicit.
- [ ] Overlapping endpoints have consistent security/error semantics.
- [ ] No existing client is broken without a versioned migration path.

## Verification

OpenAPI/SDK contract tests, REST/GraphQL authorisation tests, parity tooling and full repository validation.
