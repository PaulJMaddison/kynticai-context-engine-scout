# WP-005 — API surface hardening

## Metadata

- **Status:** Complete
- **Priority:** Medium
- **Phase:** B — Product hardening
- **Depends on:** —
- **Review gate:** standard (xhigh if scope/error contract changes)

## Context

The audit verified the v1 REST and GraphQL surfaces are comprehensive and
match the documented contracts. Four smaller gaps remain:

1. **Legacy REST endpoints lack operation IDs.** The v1 endpoints in
   `src/KynticAI.Scout.Api/Rest/VersionedRestEndpointRouteBuilderExtensions.cs`
   all use `.WithName(...)`, but the legacy endpoints in
   `src/KynticAI.Scout.Api/Rest/RestEndpointRouteBuilderExtensions.cs`
   (15 `/api/rest/...` endpoints: context, facts, sales package, audit, saas
   overview, recompute, connector plugins/register/validate/health, blueprint
   4-ops, selector preview/validate) have no `.WithName()`, so the generated
   OpenAPI/Swagger document has missing operation IDs for them.

2. **`ExportAuditEventsAsync` format-string handling was not fully
   re-reviewed.** `src/KynticAI.Scout.Application` (ScoutService) builds CSV
   or JSON export output; confirm that user-supplied field values cannot
   inject formula-like content (CSV injection: leading `=`, `+`, `-`, `@`
   cells) or break the serialisation contract. If CSV cells need sanitising,
   apply a documented rule (prefix guard) and test it.

3. **`SaasArchitectureOverviewAsync` content was not fully re-reviewed.** The
   ops summary/architecture overview feeds `/api/ops/summary` and GraphQL
   `SaasArchitectureOverview`; confirm the values exposed are derived from
   real state and contain no placeholder or misleading numbers.

4. **Scope alias documentation.** The code accepts two scope aliases
   (`context.recompute`, `connectors.read`) that are not listed in
   `docs/api-scopes.md`. Either document them as forward-compatible aliases or
   remove them. Do not silently keep undocumented accepted scopes in a
   public security-sensitive surface.

## Objective

Close the four small API-surface gaps so the public contract documentation,
the OpenAPI output, and the export/ops behaviour are all accurate, typed, and
tested.

## Do not do

- Do not change v1 route paths, request/response shapes, or error codes.
- Do not change the `docs/public-api-contract.md` promise of a stable contract
  except where it currently under-documents reality.
- Do not add rate limiting or auth changes here (out of scope).

## Scope / files touched

- `src/KynticAI.Scout.Api/Rest/RestEndpointRouteBuilderExtensions.cs`
- `src/KynticAI.Scout.Application/ScoutService.cs` (export + ops review)
- `src/KynticAI.Scout.Application/Auth/ApiScopes.cs` (alias handling, if a
  fix is chosen)
- `docs/api-scopes.md`
- `docs/public-api-contract.md` (only to reflect the final scope list)
- Tests:
  - `tests/KynticAI.Scout.UnitTests` (export sanitisation unit tests)
  - `tests/KynticAI.Scout.IntegrationTests` or `EndToEndTests` (legacy
    endpoint OpenAPI/operation-ID presence test, if a testable seam exists)

## Tasks

1. **Add `.WithName(...)` to every legacy `/api/rest/*` endpoint** in
   `RestEndpointRouteBuilderExtensions.cs`, using names consistent with the
   v1 naming style. Re-export OpenAPI and confirm operation IDs appear for all
   legacy endpoints.

2. **Audit and fix `ExportAuditEventsAsync`.** Read the implementation in
   `ScoutService.cs`. If the CSV path does not sanitise cells that start with
   `=`, `+`, `-`, `@`, `\t`, `\r` (CSV/formula injection), add a guard with a
   documented rule (recommended: prefix with a single quote or `'` and a note
   in the export contract). Add unit tests for both CSV and JSON paths
   including a malicious cell value. Verify the JSON path cannot emit invalid
   JSON for unusual field values.

3. **Review `SaasArchitectureOverviewAsync`.** Verify every returned field is
   backed by real persistence state (counts from DbContext/EF InMemory in
   tests). If any field is mocked or guessed, replace it with real state or
   remove it. Add a unit test asserting the shape and that empty data yields a
   valid, non-misleading summary.

4. **Reconcile scope aliases.** Decide and implement:
   - Recommended: keep the aliases for backward compatibility but document
     them in `docs/api-scopes.md` as compatibility aliases alongside the
     existing dot-form aliases; or
   - Remove the aliases and update any callers/tests.
   Update `docs/api-scopes.md` and the corresponding section of
   `docs/public-api-contract.md`.

## Acceptance criteria

- [x] Every REST endpoint (v1 and legacy) has a unique operation ID in the
      exported OpenAPI document.
- [x] CSV export is sanitised against formula injection; JSON export produces
      valid JSON for adversarial field values; both covered by tests.
- [x] `SaasArchitectureOverview` values are derived from real state; tests
      pin the shape and the empty-data behaviour.
- [x] `docs/api-scopes.md` matches the accepted scope set exactly.
- [x] Backend suite passes.

## Verification

```powershell
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj
dotnet test .\tests\KynticAI.Scout.IntegrationTests\KynticAI.Scout.IntegrationTests.csproj
dotnet test .\tests\KynticAI.Scout.EndToEndTests\KynticAI.Scout.EndToEndTests.csproj

# OpenAPI export (see scripts/export-openapi.sh for the canonical command)
```

## Notes

- Item 2 (CSV injection) is a small but real security hardening item; treat
  it as security work even though the severity is low.
- The legacy endpoints are kept for backward compatibility, so do not remove
  them in this package.
- Scope aliases were kept and documented as compatibility aliases in
  `docs/api-scopes.md`; no scope set changed.
- `scripts/export-openapi.sh` now pins `ASPNETCORE_URLS` so the API binds to
  the port the script polls on all platforms.
- Verification: slnx build 0 warnings/0 errors; Unit 112, Integration 42,
  EndToEnd 54 — all passing. OpenAPI export shows operation IDs for 37/37 v1
  and 16/16 legacy endpoints.
