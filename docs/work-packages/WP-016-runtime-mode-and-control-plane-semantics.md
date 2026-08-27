# WP-016 — Separate runtime mode from commercial/control-plane features

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** E — Architectural truth and product boundary
- **Depends on:** WP-012
- **Review gate:** xhigh (configuration contract, deployment)

## Context

`Platform.Mode=SaaS` currently describes too many independent concepts. In `Program.cs`, selecting SaaS automatically enables `SaaSControlPlane` and `HostedBillingUsage`, while `ControlPlane.Enabled` can still be false.

This permits internally contradictory states such as:

- SaaS control-plane feature enabled;
- hosted billing usage enabled;
- actual control plane disabled.

It also mixes deployment topology with business model. A customer-self-hosted production data plane is not necessarily "SaaS", and a hosted/control-plane relationship is independently configurable.

## Objective

Make configuration composable and literal.

Runtime/deployment mode should describe how Scout itself runs. Control plane, usage reporting, billing-related metadata and enterprise/private extensions should be explicit independent capabilities.

## Target model

Prefer clear runtime values such as:

- `Demo` / existing `LocalDemo` compatibility alias
- `SelfHosted` / existing `BackendOnly` compatibility alias
- an explicitly named managed data-plane mode only if it has real behavioural meaning

Then independently configure:

- control plane enabled
- usage reporting enabled
- hosted commercial metadata
- webhooks
- private extension loading

## Tasks

1. Map every use of `PlatformModes.SaaS`, `BackendOnly`, feature flags and readiness checks.
2. Define the minimum set of orthogonal configuration concepts.
3. Stop runtime mode from silently flipping unrelated commercial flags.
4. Preserve backwards compatibility for existing environment variables where practical; emit clear warnings/deprecation mapping rather than silently changing behaviour.
5. Update `appsettings*.json`, Docker, Render, env examples, setup scripts and docs.
6. Make `/api/platform/config` report the effective explicit configuration without misleading "enabled" values.
7. Add combination tests for supported and contradictory configurations.
8. Ensure production readiness validates actual required capabilities, not naming conventions.

## Acceptance criteria

- [ ] Runtime mode does not implicitly enable control-plane/billing capabilities.
- [ ] A self-hosted production Scout deployment has a clear supported configuration.
- [ ] Optional control-plane integration is independently enabled.
- [ ] Existing configuration either remains compatible or has documented migration.
- [ ] Readiness tests cover the supported configuration matrix.
- [ ] Documentation no longer uses SaaS as shorthand for every production deployment.

## Verification

Run production-readiness unit/E2E tests across the configuration matrix, deployment config checks and full GCP exact-SHA sign-off.
