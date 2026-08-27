# WP-021 — Separate source truth from derived Scout state in terminology and contracts

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** G — Semantic and documentation truth
- **Depends on:** WP-014, WP-015
- **Review gate:** xhigh (data semantics, upgrade compatibility)

## Context

The Scout → Fortress continuity design correctly treats retained source evidence, exact payload text and generation membership as the replay basis. Scout-derived snapshots, facts and fallback relationship weights are not Fortress canonical truth.

Some current docs nevertheless call `ContextFact` / `ContextSnapshot` the "canonical semantic record". That wording can encourage consumers or migration tooling to treat a materialised Scout view as authoritative source truth.

## Objective

Establish precise terminology throughout code comments, docs and public contracts:

- **source evidence / retained capture evidence** — what Scout captured from the source;
- **materialised Scout context** — facts/snapshots derived from evidence;
- **relationship/evidence view** — public Scout interpretation;
- **canonical/private analysis** — only where an approved public boundary statement genuinely requires the term.

Do not rename stable public types merely for aesthetics unless the semantic risk justifies a versioned contract change.

## Tasks

1. Search all uses of `canonical`, `source of truth`, `truth`, `derived`, `snapshot` and `evidence`.
2. Classify each statement as data-source authority, Scout materialisation, public fallback or private/Fortress authority.
3. Rewrite misleading docs/comments.
4. Add an architecture table showing what is replayable source evidence versus recalculable derived state.
5. Ensure migration/export documentation uses retained evidence/generation membership as the upgrade basis.
6. Check API descriptions so consumers do not infer a snapshot supersedes source ownership.
7. Add tests only where terminology corresponds to an executable invariant (for example export must not rebuild from old derived snapshots).

## Acceptance criteria

- [ ] No current document calls derived Scout facts/snapshots canonical source truth.
- [ ] Source evidence and derived context are clearly separated.
- [ ] Scout→Fortress upgrade docs remain consistent with exact-evidence/generation semantics.
- [ ] Stable public type names remain compatible unless a deliberate versioned change is approved.

## Verification

Repo-wide terminology sweep plus existing continuity/upgrade contract tests.
