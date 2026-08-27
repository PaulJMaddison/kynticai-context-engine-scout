# WP-024 — Make Scout core, examples, tools and integrations obvious

## Metadata

- **Status:** Complete
- **Priority:** Medium
- **Phase:** H — Maintainability and developer experience
- **Depends on:** WP-013, WP-014, WP-015, WP-023
- **Review gate:** xhigh for physical moves affecting packages/API; otherwise standard

## Context

Scout is a large open-source monorepo containing the core engine, admin web app, docs site, Discovery tooling, connector tooling, n8n integrations, SDKs, migration/cutover tooling, commercial/pilot docs and several reference use cases.

The monorepo itself is not the problem. The problem is that a new contributor can struggle to distinguish "Scout itself" from "an example built with Scout" or "tooling that supports Scout".

## Objective

Keep the useful monorepo while making conceptual boundaries visible from the directory tree, README and build graph.

## Desired categories

Use the existing structure where practical, but make these categories unmistakable:

- **core/runtime** — Scout API/application/domain/infrastructure
- **SDKs**
- **reference applications/examples**
- **integrations**
- **developer/connector tooling**
- **operations/deployment tooling**
- **documentation**

Do not perform gratuitous moves that create package churn without improving clarity.

## Tasks

1. Produce a repo topology map with every top-level package/app/tool assigned to one category.
2. Identify misleading locations exposed by earlier work packages.
3. Move only items whose current location materially misrepresents their role.
4. Add top-level README/AGENTS guidance explaining the categories.
5. Update solution/project/package references after any move.
6. Preserve package names unless a separate compatibility decision requires change.
7. Verify normal local build/test scripts discover every moved package.
8. Remove obsolete duplicate assets/packages only when equivalence is proven.

## Implementation note (2026-08-27)

The repository now has an explicit `examples/` category. The fictional
sales/next-action implementation lives in
`examples/KynticAI.Scout.Reference.Sales`, is built by the solution, and is referenced
only by tests that deliberately exercise the example. It is not registered by Scout's
production/core dependency injection. The root README documents the distinction.

## Acceptance criteria

- [ ] A contributor can identify Scout core without reading the whole repo.
- [ ] Reference applications are clearly labelled and cannot be mistaken for platform behaviour.
- [ ] Integrations and tooling are clearly separated from runtime code.
- [ ] All build/test/validation scripts still cover the full intended repository.
- [ ] No public package path changes without compatibility notes.

## Verification

Full local repository tree/build/test/package validation, including docs-site and all TypeScript packages.
