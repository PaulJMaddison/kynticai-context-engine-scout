# GCP pre-cloud validation

Last updated: 2026-08-17

This is the required disposable-cloud validation for Scout before a production Scout -> Fortress cutover or a final engineering sign-off is considered proven.

The runner is intentionally repo-wide. It validates the .NET data plane, PostgreSQL migration/startup path, React application, public TypeScript packages, n8n integrations, the public Scout metadata Discovery MCP, and the generic local Discovery Agent. The commercial KynticAI Discovery MCP buyer workflow is private Fortress software and is not part of this public Scout gate.

The cutover-specific purpose remains to prove the parts of Scout that must be correct before customer source ownership moves: PostgreSQL migrations, FULL_SOURCE capture evidence, generation membership, lease exclusion, durable cutover ownership, exact export selection, restart behaviour and cost-bounded deployment.

The core suite uses Scout's `mock` LLM provider. A paid or GPU model adds cost and another failure mode without improving this proof.

## What this validates

The checked-in automated core gate runs:

1. Pinned .NET 10.0.203 and Node.js 24.19.0 toolchains.
2. Clean-checkout SHA verification when `SCOUT_EXPECTED_SHA` is supplied.
3. .NET restore and `dotnet format --verify-no-changes`.
4. Release build with warnings as errors.
5. Full deterministic .NET test suite.
6. Build/test validation for every committed TypeScript package under `packages/typescript`.
7. React web lint, build and tests.
8. Generic Discovery Agent build, tests and a Tier-1 smoke audit against the exact checkout.
9. Package dry-runs for the public SDK and both n8n packages.
10. `dotnet ef migrations has-pending-model-changes` against the Scout runtime model.
11. PostgreSQL 16 startup.
12. The real `MigrateAsync` path used by normal Scout API startup.
13. Verification that `connector_capture_ownership` and its indexes exist.
14. Scout API readiness against PostgreSQL with the mock LLM provider.
15. Focused continuity, queue, credential-hash, CORS and machine-token failure-path tests.
16. A tracked-file cleanliness check after validation.

The large synthetic source/cutover acceptance matrix below remains an explicit second layer because connector-specific fixture credentials and large datasets are deliberately not committed to the public repository.

## Cost envelope

Use a dedicated or disposable Google Cloud project where possible.

Default test location:

- region: `europe-west2` (London)
- zone: `europe-west2-b`

Default VM:

- `e2-standard-4`
- 4 vCPU / 16 GB RAM
- 50 GB balanced persistent boot disk
- no GPU
- no TPU
- no Cloud SQL
- one VM only
- maximum runtime: 2 hours
- termination action: `DELETE`

Optional million-row scale pass:

- `e2-standard-8`
- 8 vCPU / 32 GB RAM
- same 2 hour deletion limit

Cloud pricing changes. Check the live Google Cloud price/calculator for the selected project and zone immediately before running.

The administrative budget is fixed by the checked-in helper at **25 billing-account currency units** with alerts at 50%, 80% and 100%.

A Google Cloud budget is an alerting mechanism. It does not itself stop resources. The hard practical guardrails in this test are therefore:

- the setup script refuses machine types other than `e2-standard-4` and `e2-standard-8`;
- the setup script refuses a runtime other than two hours;
- Compute Engine is configured to delete the VM at the runtime limit;
- no GPU is provisioned;
- no managed database is provisioned;
- no application firewall port is opened by the harness;
- teardown explicitly deletes the named VM and fails if another precloud-labelled VM remains.

For a normal branch validation, the expected bill should be far below the 25-unit alerting budget. Stop and investigate if it is not.

## Prerequisites

Workstation/control machine:

- Google Cloud CLI installed and authenticated.
- A Google Cloud project with billing enabled.
- Permission to create Compute Engine instances.
- If using the budget helper, permission to create billing budgets and the billing account ID.

The workstation does **not** need enough disk space to restore/build Scout. The runner clones, restores, builds and tests on the disposable GCP VM.

Do not use production customer data, production source credentials or production cutover tokens in this test.

## 1. Create the budget guardrail

Set:

```bash
export GCP_PROJECT_ID="your-disposable-project"
export GCP_BILLING_ACCOUNT_ID="your-billing-account-id"
```

Then:

```bash
bash scripts/cloud-tests/gcp-precloud-budget.sh
```

The helper is idempotent by display name. It deliberately refuses a different checked-in budget amount rather than allowing an accidental expensive run to be authorised by an environment variable.

## 2. Provision the disposable VM

```bash
export GCP_PROJECT_ID="your-disposable-project"
bash scripts/cloud-tests/gcp-precloud-setup.sh
```

For the optional million-row pass only:

```bash
SCOUT_MACHINE_TYPE=e2-standard-8 \
GCP_PROJECT_ID="your-disposable-project" \
bash scripts/cloud-tests/gcp-precloud-setup.sh
```

The VM has no Scout/API firewall rule. The runner uses `gcloud compute ssh` and the API remains bound to localhost on the VM.

## 3. Run the repo-wide core gate

The default branch is `main`.

For a final sign-off, pin the exact revision so the runner refuses to validate a moving or mistaken branch:

```bash
GCP_PROJECT_ID="your-disposable-project" \
SCOUT_BRANCH="main" \
SCOUT_EXPECTED_SHA="<exact-main-sha>" \
bash scripts/cloud-tests/gcp-precloud-run.sh
```

To validate a review branch before merging:

```bash
GCP_PROJECT_ID="your-disposable-project" \
SCOUT_BRANCH="agent/final-engineering-signoff" \
SCOUT_EXPECTED_SHA="<exact-branch-sha>" \
bash scripts/cloud-tests/gcp-precloud-run.sh
```

A successful automated run ends with:

```text
REPO_WIDE_PRECLOUD_VALIDATION=PASS
```

Do not accept a partial run as a pass. In particular, the .NET and Node test/build gates, model/migration check, PostgreSQL startup/migration and focused safety tests are mandatory.

The generic Discovery Agent currently installs with `--package-lock=false` because its previous lockfile described the commercial buyer-facing Discovery MCP wrapper that was removed from Scout. A clean replacement lockfile should be generated and committed from a successful dependency resolution before publishing that package. This is a reproducibility concern, not permission to restore the private buyer workflow to Scout.

## 4. Synthetic capture and cutover acceptance matrix

The following tests are required before production cutover. They intentionally use generated records and test-only connector credentials.

### A. FULL_SOURCE exact evidence

For each connector path that will be enabled in production, capture a known synthetic source set and prove:

- checkpoint `CoverageScope` is `FULL_SOURCE`;
- checkpoint `PayloadStorageContract` is `exact-text.v1`;
- checkpoint `GenerationMembershipContract` is `generation-membership.v1`;
- every selected generation member has an exact payload evidence row;
- SHA-256 in the capture envelope matches the retained exact payload text;
- a genuinely empty source completes a generation with zero members rather than being treated as an incomplete capture.

### B. Anti-resurrection

1. Complete generation 1 with records `A` and `B`.
2. Change the source so only `A` exists.
3. Complete generation 2.
4. Start, but do not complete, generation 3.
5. Run upgrade export.

Pass criteria:

- the persisted ownership binding selects generation 2;
- the export contains `A` and not `B`;
- incomplete generation 3 cannot move the selected generation;
- exported row count equals generation-2 membership count exactly.

Any appearance of `B` is a hard failure because it demonstrates stale snapshot resurrection.

### C. Cutover concurrency

Run 8, then 32, concurrent capture attempts while starting upgrade export.

Pass criteria:

- no two workers successfully own the same checkpoint lease;
- export waits for an active lease rather than stealing it;
- once the pause transaction commits, no worker reaches credential retrieval or source I/O;
- exactly one ownership row exists per connector installation;
- state is `ScoutPausedForCutover`;
- selected generation, snapshot completion and high-water hash match the locked checkpoint;
- retry with the same epoch and token succeeds deterministically;
- retry with a different epoch or token is rejected;
- a `FortressOwned` binding cannot be overwritten by Scout.

### D. Export tamper/fail-closed tests

Independently alter each of the following in a copied test database and require export failure:

- ownership tenant;
- ownership selected generation;
- checkpoint completion timestamp;
- cutover epoch;
- cutover token hash;
- generation membership count;
- exact-payload SHA-256;
- capture namespace/object type/record id;
- capture coverage/storage contract.

No case may silently produce a smaller or different handoff.

### E. Crash and restart

Inject process termination at these points:

1. while a capture worker owns a lease;
2. while the export pause transaction is waiting for a lease;
3. immediately after the pause transaction commits;
4. during JSONL export;
5. after JSONL is written but before the operator transfers ownership to Fortress;
6. after a recompute job is persisted but before it reaches the in-memory queue;
7. while a recompute selector execution is running.

Pass criteria:

- PostgreSQL recovery leaves a valid transactional state;
- after a committed pause, Scout remains paused across restart;
- a retry using the same cutover epoch/token binds to the same snapshot;
- no restart resumes source capture while ownership is paused or Fortress-owned;
- a failed export never silently unpauses Scout;
- persisted Pending/stranded Running recompute jobs are rediscovered by the recovery worker;
- already-succeeded selector executions are reused from durable result state instead of being called again;
- completed/failed recompute jobs remain idempotent no-ops on duplicate delivery.

### F. Required 100,000-row proof

Run with at least 100,000 synthetic source records across at least three source object types.

Record:

- capture duration;
- export duration;
- peak process memory;
- PostgreSQL database size before and after capture;
- generation member count;
- exact evidence count;
- JSONL row count;
- JSONL SHA-256;
- manifest row count and hash.

Pass criteria are correctness first. All row counts and hashes must reconcile exactly. Performance numbers are evidence, not permission to weaken a correctness check.

### G. Optional 1,000,000-row proof

Run only after the 100k pass and only on `e2-standard-8` unless a smaller machine has already demonstrated sufficient headroom.

Use the same reconciliation criteria. The VM remains under the same two-hour automatic-delete guardrail, so a workload that cannot complete inside the window fails the chosen test envelope rather than extending it automatically.

## 5. Model-backed test, if required later

Do **not** put a model into the core Scout cutover test.

If a future release needs to validate an AI response path as well, make that a separate test stage with:

- a separately approved model;
- synthetic inputs only;
- a request-count cap;
- a token cap;
- an independent budget;
- no ability for model failure to mask capture/cutover failures.

The Scout -> Fortress ownership proof must remain deterministic and model-independent.

## 6. Teardown

Always run teardown, even when a test fails:

```bash
GCP_PROJECT_ID="your-disposable-project" \
bash scripts/cloud-tests/gcp-precloud-teardown.sh
```

Then check the project in Billing/Asset Inventory and confirm there are no unintended resources left.

Do not rely on the two-hour auto-delete as the normal cleanup path. It is the last-resort cost stop.

## Evidence to retain

Retain only non-customer proof material:

- exact tested Git SHA and branch;
- .NET, Node.js and npm versions;
- test/build/lint result summary;
- migration/model result;
- synthetic row-count/hash reconciliation;
- timings and resource measurements;
- final teardown confirmation.

Do not copy exact customer payloads, credentials, cutover tokens or production database dumps into build artifacts or cloud logs.

## Release gate

A release is not pre-cloud validated until all of the following are true:

- final static/review diff is clean;
- exact SHA is recorded;
- .NET formatting check passes;
- Release build passes with warnings as errors;
- full deterministic .NET suite passes;
- every committed Node/TypeScript surface builds and tests;
- web lint/build/tests pass;
- generic Discovery Agent smoke audit passes;
- EF has no pending model changes;
- PostgreSQL migrations apply from the supported baseline;
- API readiness succeeds against PostgreSQL;
- ownership migration/table/indexes exist;
- required focused failure-path tests pass;
- 100k synthetic capture/export reconciliation passes before a real production cutover;
- anti-resurrection passes;
- concurrent pause/cutover passes;
- crash/restart passes;
- teardown is confirmed;
- actual cloud spend is reviewed and remains inside the approved envelope.

If any item is missing, record it as unvalidated rather than inferring a pass from the other checks.
