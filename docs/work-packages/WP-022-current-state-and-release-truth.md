# WP-022 — Remove stale repository state and correct release truth

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** G — Semantic and documentation truth
- **Depends on:** WP-012
- **Review gate:** standard

## Context

The repository contains multiple places that claim to describe current engineering state:

- `AGENTS.md`
- `SESSION.md`
- roadmap
- work-package backlog
- changelog
- release notes

They have drifted.

Examples found in the 2026-08-27 review:

- root `SESSION.md` still describes an old pre-cloud feature branch/PR as "Current branch truth";
- `AGENTS.md` lists work as upcoming that the backlog/roadmap marks complete;
- v2.9.0 release notes say CI/CD was re-enabled while the actual workflow files are deliberately `.disabled` and WP-007 says live Actions remained disabled.

Stale state files are especially dangerous in an agent-driven repository because an agent may trust them more than current code.

## Objective

Reduce project-state sources of truth and make every retained current-state document factually correct.

## Target ownership

- product naming/boundary → canonical naming map;
- shipped capability → code/tests + README/changelog/release notes;
- planned/current work → GitHub issues/projects + work-package index;
- historical engineering sessions → dated history, never "current branch truth".

## Tasks

1. Decide whether `SESSION.md` should be removed, archived under dated history, or rewritten as non-authoritative historical notes.
2. Remove stale branch/PR/current-state assertions from public root docs.
3. Remove sprint-status duplication from `AGENTS.md`; keep only durable contributor/agent rules.
4. Correct v2.9.0 CI wording without rewriting legitimate history.
5. Reconcile current roadmap/backlog status.
6. Add a short "sources of truth" section to the work-package index or contribution docs.
7. Search for old branch names, obsolete issue states and contradictory "current" claims.
8. Ensure historical records are clearly dated and cannot be mistaken for live instructions.

## Acceptance criteria

- [ ] No root document claims an obsolete branch/PR is current.
- [ ] AGENTS contains durable rules rather than stale sprint tracking.
- [ ] Release notes describe CI state truthfully.
- [ ] Work status has one current home.
- [ ] Historical notes are visibly historical.

## Verification

Repo-wide searches for known obsolete branch names/status phrases and links; docs build/reference checks.
