# WP-020 — Correct production backup and source-system responsibility

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** F — Production correctness
- **Depends on:** WP-014
- **Review gate:** standard (operations/data ownership)

## Context

Some production documentation tells Scout operators to back up both the Scout database and the "customer operations source database". That instruction reflects the current fictional CustomerOps demo architecture rather than the real enterprise ownership model.

Scout should not take operational ownership of Salesforce, SAP, a customer's warehouse or another upstream source simply because Scout reads from it.

## Objective

Document a clean responsibility boundary:

Scout deployment owners are responsible for Scout-owned state/evidence, configuration and cryptographic material. Upstream source systems remain under their existing customer ownership and recovery processes.

## Tasks

1. Reconcile backup/restore wording across hosted deployment, production checklist, paid-pilot docs and support docs.
2. Define Scout-owned recoverable assets:
   - Scout PostgreSQL state
   - retained source-capture evidence where stored by Scout
   - capture checkpoints/ownership state
   - Data Protection key ring
   - local licence/configuration required to restore service
3. Define upstream dependencies without claiming ownership of their backups.
4. Add a restore validation sequence proving Scout can be restored and reconnect to an already-restored/available source.
5. Distinguish backup from replay: retained Scout evidence is not necessarily a full backup of the source system.
6. Remove demo-specific CustomerOps responsibility from generic production docs.

## Acceptance criteria

- [ ] No generic Scout production document instructs Scout operators to back up upstream enterprise systems.
- [ ] Scout-owned backup scope is explicit.
- [ ] Evidence retention is not described as a source-system backup unless a specific connector contract proves that.
- [ ] Restore ownership is suitable for a real customer RACI.

## Verification

Docs/reference check plus rehearsal-script updates where appropriate. Do not require access to a real customer system.
