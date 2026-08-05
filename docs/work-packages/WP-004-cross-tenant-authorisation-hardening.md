# WP-004 — Cross-tenant authorisation hardening

## Metadata

- **Status:** Complete
- **Priority:** High
- **Phase:** B — Product hardening
- **Depends on:** —
- **Review gate:** xhigh (security-sensitive)

## Context

In `src/KynticAI.Scout.Api/Rest/VersionedRestEndpointRouteBuilderExtensions.cs`
there are two tenant-resolution helpers:

- `ResolveTenantSlug` (line 755) — resolves the actor's tenant and enforces a
  cross-tenant ownership check.
- `ResolveRequestedTenantSlug` (line 777) — returns any requested slug WITHOUT
  the cross-tenant ownership check that the other helper enforces.

`ResolveRequestedTenantSlug` is used on admin-group endpoints at lines 432,
448, 462, 477, 499, and 514, whose declared roles include `TenantAdmin` and
`IntegrationAdmin` (role matrix at lines 84-131). The affected endpoints
include `/audit-events/export`, `/admin/organisation`, `/admin/users`,
PATCH `/admin/users/{id}`, `/blueprints`, `/governance/policies`,
`/api-clients`, and `/webhook-signing-secrets`.

The audit's assessment: role checks still apply and the DB queries are
tenant-scoped, so the exposure is limited to a tenant admin passing another
tenant's slug. However, the requested slug is never verified against the
actor's tenant, which is a defence-in-depth gap and a possible information-
disclosure vector (e.g. exporting another tenant's audit events or reading
another tenant's organisation settings / user list).

## Objective

Close the cross-tenant authorisation gap by enforcing the same tenant-
ownership rule on every endpoint that uses `ResolveRequestedTenantSlug`, and
prove it with tests. Confirm intended behaviour first: either those admin
endpoints are platform-owner-only (then the fix is to restrict the role set),
or they are tenant-scoped (then the fix is to bind the slug to the actor's
tenant).

## Do not do

- Do not change the public URL routes or request/response shapes.
- Do not weaken any existing role check.
- Do not introduce a behaviour change without a test that pins the new
  behaviour (a cross-tenant request must be rejected).
- Do not treat this as complete until the intended-owner decision is recorded.

## Scope / files touched

- `src/KynticAI.Scout.Api/Rest/VersionedRestEndpointRouteBuilderExtensions.cs`
- Role/scope constants if the decision is platform-owner-only
  (see `Auth/RoleNames.cs` or equivalent)
- Tests:
  - `tests/KynticAI.Scout.EndToEndTests` (add cross-tenant rejection tests)
  - `tests/KynticAI.Scout.IntegrationTests` if an admin V1 test host exists
- `docs/public-api-contract.md` (only if the role matrix for an endpoint
  changes)
- `CHANGELOG.md` (`[Unreleased]` entry if behaviour changes)

## Tasks

1. **Confirm intended behaviour.** Determine whether the admin endpoints are
   meant to be:
   - (a) platform-owner only — then remove `TenantAdmin`/`IntegrationAdmin`
     from their role lists and enforce platform-owner scope; or
   - (b) tenant-scoped — then `ResolveRequestedTenantSlug` must behave exactly
     like `ResolveTenantSlug` (verify the actor's tenant matches the requested
     slug and throw the same `authorization.denied` 403 otherwise).
   Record the decision in the session log. Recommended: (b) — reuse the
   existing `ResolveTenantSlug` helper and delete the divergent
   `ResolveRequestedTenantSlug`, unless there is a deliberate reason a
   platform operator may query another tenant's slug.

2. **Implement the fix.** Prefer deleting `ResolveRequestedTenantSlug` and
   routing all call sites through `ResolveTenantSlug`. If a platform-operator
   cross-tenant path genuinely exists, model it explicitly (e.g. a role check
   for `platform_owner` before allowing a foreign slug) rather than silently
   allowing it for tenant admins.

3. **Add regression tests.** Using the existing `WebApplicationFactory`
   harness in `tests/KynticAI.Scout.EndToEndTests`:
   - A `tenant_admin` authenticated as tenant A calling
     `/api/v1/audit-events/export?tenantSlug=<tenant B>` must receive 403
     `authorization.denied`.
   - Repeat for at least one of `/admin/organisation`, `/admin/users`, and
     `/blueprints` with the same cross-tenant slug.
   - Same-tenant calls must still succeed (no regression).
   - If platform-owner cross-tenant access is kept, add a test that a
     `platform_owner` may read a foreign slug while a `tenant_admin` may not.

4. **Update contract docs if needed.** If role matrixes change, update the
   route/role table in `docs/public-api-contract.md` and the API scopes in
   `docs/api-scopes.md` accordingly.

## Acceptance criteria

- [x] No call site uses an unresolved/foreign tenant slug without an
      ownership check.
- [x] New tests assert cross-tenant admin requests are rejected with 403 and
      same-tenant requests still work.
- [x] `ResolveRequestedTenantSlug` is either deleted or explicitly justified
      and covered by a platform-owner test.
- [x] Public API docs match the final role matrix.
- [x] Full backend suite passes (Unit + Integration + SDK + EndToEnd).

## Verification

```powershell
dotnet test .\tests\KynticAI.Scout.EndToEndTests\KynticAI.Scout.EndToEndTests.csproj
dotnet test .\tests\KynticAI.Scout.IntegrationTests\KynticAI.Scout.IntegrationTests.csproj
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj
dotnet test .\tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj
```

## Notes

- **Decision (recorded):** option (b) was adopted. The affected admin
  endpoints are tenant-scoped; `ResolveRequestedTenantSlug` was deleted and
  all six call sites now use `ResolveTenantSlug`. Platform owners and system
  actors keep explicit cross-tenant access (covered by a platform-owner test);
  tenant admins and integration admins are rejected with `403
  authorization.denied`.
- This is the single highest-value code finding from the audit. Treat it as
  a security fix, not a refactor.
- The `.env` in the repo contains only dev credentials; do not introduce any
  production secrets while testing.
- Verification: `dotnet build KynticAI.Scout.slnx` 0 warnings/0 errors; Unit
  107, SDK 13, Integration 42 (incl. 4 new cross-tenant tests), EndToEnd 54 —
  all passing.
