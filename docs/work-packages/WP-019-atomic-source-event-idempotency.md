# WP-019 — Atomic cross-instance source-event idempotency

## Metadata

- **Status:** Implementation complete; real local PostgreSQL concurrency proof pending
- **Priority:** Critical before horizontally scaled webhook/event ingestion
- **Phase:** F — Production correctness
- **Depends on:** —
- **Related issue:** #40 (remains OPEN until executable PostgreSQL proof passes)
- **Review gate:** xhigh (distributed correctness, persistence, side effects)

## Status note (2026-08-27)

The implementation is already in place and was verified this session:

- `IngestSourceSystemEventAsync` opens a transaction-scoped `pg_advisory_xact_lock` keyed on
  `(TenantId, SourceSystem, EventId)` and runs the authoritative duplicate check only after
  acquiring that lock, so concurrent duplicate submissions across instances serialise at the
  PostgreSQL boundary (not an in-process semaphore).
- Exact duplicates return an idempotent `SourceSystemEventAcceptedResult(IsDuplicate: true)`;
  a conflicting payload for the same logical event raises `SourceSystemEventConflictException`
  (documented conflict policy; historical truth is never silently overwritten).
- Explicit `DataSourceId` is part of immutable event identity and a different explicit value is
  a conflict. `ObservedAtUtc` is intentionally delivery-tolerant: retries may differ because it
  describes source observation rather than event identity, while the first retained event keeps
  the authoritative observation timestamp.
- Persistence and all creation-only side effects (audit, usage, user signal, selector
  executions, recompute job) commit in one atomic transaction; the recompute queue enqueue
  happens once, after commit.
- The backing DB unique index on `(TenantId, SourceSystem, EventId)` is present
  (`SourceSystemEventConfiguration`).

The remaining gap is the executable real-PostgreSQL concurrency proof called for by Tasks 6 and
10. It has not yet been run because a local PostgreSQL service was unavailable during the previous
pass. The proof must be run against real local PostgreSQL (for example an existing local Docker
PostgreSQL service) before issue #40 is closed. If local PostgreSQL is unavailable, keep this
package Partial and issue #40 open; do not provision cloud infrastructure merely to satisfy this
proof.

## Context

Issue #40 records a real race in `IngestSourceSystemEventAsync`.

Two Scout instances can concurrently observe that a logical event does not exist, both prepare the insertion/related work, and then race on the database uniqueness constraint. The database prevents duplicate rows, but the losing request can surface a unique-key failure and side-effect handling is not atomically proven.

A process-local semaphore is explicitly unacceptable because it only works on one node.

## Objective

Make source-event ingestion truly idempotent at the PostgreSQL/application transaction boundary.

For a logical key `(tenant, sourceSystem, eventId)`:

- exactly one canonical event is persisted;
- every concurrent duplicate receives a successful idempotent result for that canonical event;
- recompute/relationship/other downstream side effects happen once;
- unrelated database errors are never misclassified as duplicates.

## Preferred implementation direction

Use either:

1. a PostgreSQL-native atomic insert/upsert that returns the canonical row; or
2. a narrowly classified unique-constraint retry using a fresh transaction/DbContext followed by canonical reload.

Choose based on transactional clarity and EF/Npgsql behaviour. Do not implement an in-process lock as the production solution.

## Tasks

1. Trace the exact existing transaction and side-effect sequence for direct ingestion and signed webhook ingestion.
2. Identify the database constraint name and classify only that unique violation.
3. Make persistence and creation-only side effects atomic.
4. Define the response semantics for first receipt and duplicate receipt.
5. Ensure a duplicate with conflicting payload for the same logical event follows an explicit safe policy; do not silently overwrite historical truth.
6. Add PostgreSQL concurrency tests using two independent DbContexts/connections and a barrier so requests genuinely race.
7. Test direct event ingestion and signed webhook ingestion.
8. Test unrelated `DbUpdateException`/provider failures still fail normally.
9. Test restart/retry behaviour and audit/recompute side-effect counts.
10. Close issue #40 only after executable PostgreSQL proof passes.

## Acceptance criteria

- [ ] 2, 8 and 32 concurrent duplicate submissions result in one stored logical event.
- [ ] All callers receive successful/idempotent canonical responses where appropriate.
- [ ] Creation-only side effects occur once.
- [ ] Conflicting duplicates fail or report conflict according to the documented policy.
- [ ] Non-unique DB failures are not swallowed.
- [ ] Tests use independent PostgreSQL connections, not EF InMemory.
- [ ] Signed-webhook path has equivalent proof.

## Verification

This package requires real PostgreSQL. Run the concurrency tests against a local PostgreSQL instance or an existing local Docker PostgreSQL service. If neither is available, report the provider-specific proof as blocked and leave issue #40 open.
