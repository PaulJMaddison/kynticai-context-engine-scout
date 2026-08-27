# WP-019 — Atomic cross-instance source-event idempotency

## Metadata

- **Status:** Planned
- **Priority:** Critical before horizontally scaled webhook/event ingestion
- **Phase:** F — Production correctness
- **Depends on:** —
- **Related issue:** #40
- **Review gate:** xhigh (distributed correctness, persistence, side effects)

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

This package requires real PostgreSQL and must be executed on disposable GCP infrastructure when local constraints apply. Pin the exact SHA and include the concurrency tests in the final GCP validation gate.
