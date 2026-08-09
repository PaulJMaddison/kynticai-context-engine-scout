# Changelog

Public KynticAI Scout changes are documented in this file. Private package changes should stay in private changelogs.

## Canonical changelog

The canonical and complete changelog for this repository is the root [`CHANGELOG.md`](../../CHANGELOG.md). It follows Keep a Changelog and Semantic Versioning, covers every release from v0.1.0 to the current version, and carries the `[Unreleased]` entries. It is the single source of truth for release history.

This file is an index of the per-release notes in this directory, not a separate release history.

## Release notes

| Release | Notes |
| --- | --- |
| v2.10.0 | [v2.10.0.md](v2.10.0.md) |
| v2.9.0 | [v2.9.0.md](v2.9.0.md) |
| v2.8.0 | [v2.8.0.md](v2.8.0.md) |
| v2.7.0 | [v2.7.0.md](v2.7.0.md) |
| v2.6.0 | [v2.6.0.md](v2.6.0.md) |
| v2.5.1 | [v2.5.1.md](v2.5.1.md) |
| v2.5.0 | [v2.5.0.md](v2.5.0.md) |
| v2.4.1 | [v2.4.1.md](v2.4.1.md) |
| v2.4.0 | [v2.4.0.md](v2.4.0.md) |
| v2.3.0 | [v2.3.0.md](v2.3.0.md) |
| v2.2.0 | [v2.2.0.md](v2.2.0.md) |
| v2.1.0 | [v2.1.0.md](v2.1.0.md) |
| v2.0.0 | [v2.0.0.md](v2.0.0.md) |
| v1.1.0 | [v1.1.0.md](v1.1.0.md) |
| v1.0.0 | [v1.0.0.md](v1.0.0.md) |

The demo-era releases (v0.1.0, v0.1.1) and the v2.1.1 patch release have entries in the root `CHANGELOG.md` but no separate note in this directory; the root changelog remains authoritative for them.

## Maintaining this index

When a new release is cut, follow [docs/releases/release-process.md](release-process.md):

1. Add the full release entry to the root `CHANGELOG.md`.
2. Add a `vX.Y.Z.md` note in this directory.
3. Add a row to the release-notes table above.

## Template

Use this template when adding a new release entry:

```markdown
## [X.Y.Z] - YYYY-MM-DD

Public Scout release.

### Open-Source (`scout`)

#### Added
- Description of new features or capabilities.

#### Changed
- Description of changes to existing functionality.

#### Fixed
- Description of bug fixes.

#### Removed
- Description of removed features or deprecated items.

#### Security
- Description of security-related changes.

#### Breaking Changes
- Description of breaking changes with migration guidance.

### Private Package Coordination
- Keep private package details in private changelogs.
```

### Categories

| Category | Use for |
|---|---|
| **Added** | New features, endpoints, connectors, documentation |
| **Changed** | Changes to existing functionality, behaviour, or configuration |
| **Fixed** | Bug fixes, regression fixes, performance fixes |
| **Removed** | Removed features, deprecated endpoints, deleted files |
| **Security** | Security patches, boundary enforcement, secret hygiene |
| **Breaking Changes** | API contract changes, schema migrations, SDK interface changes that require consumer updates |

### Guidelines

1. Write entries in the **past tense** ("Added", "Fixed", not "Adds", "Fixes").
2. Group entries by repository, then by category.
3. Link to relevant PRs or issues where possible.
4. Note any migration steps required under Breaking Changes.
5. Keep entries concise but specific enough to be actionable.
