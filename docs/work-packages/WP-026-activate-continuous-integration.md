# WP-026 — Activate continuous integration when the GitHub account permits it

## Metadata

- **Status:** Blocked — external GitHub Actions/account restriction
- **Priority:** High once unblocked
- **Phase:** H — Maintainability and developer experience
- **Depends on:** WP-022 and resolution of the external GitHub Actions restriction
- **Review gate:** standard for activation; xhigh if workflow scope/security changes

## Context

WP-007 hardened reference CI/release workflows but deliberately kept them as `.yml.disabled` because GitHub Actions could not run under the account state at the time.

The repository therefore has a strong validation design but no automatic pull-request/main enforcement.

Local deterministic validation remains the current proof path. Ordinary deterministic checks should execute automatically on every change once the external GitHub Actions restriction is resolved.

## Objective

Activate safe CI without weakening the existing opt-in rules for expensive/browser/external-provider proofs.

## Tasks

1. Confirm GitHub Actions can actually start jobs on the repository before changing files.
2. Review the disabled CI against current repo topology after WP-024.
3. Activate a PR/main workflow covering:
   - .NET restore + Release build warnings-as-errors
   - deterministic unit/SDK/integration/E2E tests
   - web lint/test/build
   - all supported TypeScript package build/tests
   - docs-site build
   - public-safety scan
   - package contract/parity checks
4. Keep browser, Docker/PostgreSQL scale and external/private cross-repo proofs opt-in or separate.
5. Use dependency caching only where it cannot produce stale correctness.
6. Add branch protection/required-check guidance only after a real green run.
7. Keep release publishing separate and explicit; do not automatically publish packages merely by activating CI.
8. Update docs/release notes only after observing a real successful Actions run.

## Acceptance criteria

- [ ] Active `.github/workflows/*.yml` files exist.
- [ ] At least one real PR/main run completes successfully.
- [ ] Public-safety and deterministic test gates are required before merge where repository settings allow.
- [ ] Expensive/external proofs remain explicitly gated.
- [ ] Documentation no longer says CI is disabled.
- [ ] Release publishing is not accidentally enabled.

## Verification

The verification is the live GitHub Actions run itself plus comparison with the local validation matrix. Do not mark complete based only on YAML inspection.
