# WP-006 — SDK compatibility fix (Fact value type)

## Metadata

- **Status:** Complete
- **Priority:** Medium
- **Phase:** B — Product hardening
- **Depends on:** —
- **Review gate:** xhigh (public SDK contract change)

## Completion notes

- `ContextFactResult.ValueType` (and `GroundedContextFactResult`) in the .NET
  SDK is now the `KynticAI.Scout.Sdk.FactValueType` enum; a tolerant
  `FactValueTypeJsonConverter` is registered on the shared serializer options.
- The SDK's NuGet package remains self-contained: the enum is defined inside
  the SDK and the package ships a single assembly with no package dependency
  (verified in the generated nuspec after removing the temporary project
  reference to Domain).
- `tests/KynticAI.Scout.Sdk.Tests/FactValueTypeContractTests.cs` pins the
  integer wire encoding (String=1, Number=2, Boolean=3, Json=4, Enum=5,
  EnumSet=6) with tolerant reader/writer coverage.
- `tests/KynticAI.Scout.EndToEndTests/SdkIntegrationE2ETests.cs` now uses the
  typed SDK instead of raw HTTP + `JsonNode`.
- TypeScript SDK: `valueType` is now the `FactValueType` string-literal union
  in `packages/typescript/scout-sdk/src/types.ts`; `npm run build` and
  `npm test` (17 tests) pass.
- Compatibility note added to `docs/sdk-development.md`; `[Unreleased]`
  CHANGELOG entry added.
- Verification: Sdk.Tests 40, EndToEnd 54, Unit 112, Integration 42 all
  green; slnx build 0 warnings / 0 errors.

## Context

The audit found the one known SDK/API contract mismatch, documented honestly
in the tests:

`tests/KynticAI.Scout.EndToEndTests/SdkIntegrationE2ETests.cs` (lines 9-18 and
57) records that the SDK types `ContextFactResult.ValueType` as `string`
while the API serialises the `FactValueType` enum as an integer. Tests that
would hit this are deliberately written against raw HTTP + `JsonNode`
assertions instead of the typed SDK, so the gap is currently hidden behind a
test workaround rather than fixed.

This is a public SDK contract defect: .NET SDK and TypeScript SDK consumers
will mis-read fact value types from the API. Any fix is a compatibility-
sensitive public shape change and needs a compatibility note per the
`AGENTS.md` rule ("Do not change ... public API contracts, or SDK shapes
without compatibility notes and tests").

## Objective

Make the SDK deserialise `FactValueType` correctly from the API and remove
the raw-HTTP test workaround, replacing it with a typed contract test that
pins the wire format on both sides.

## Do not do

- Do not change the API wire format for `FactValueType` unless you first
  confirm no consumers depend on the current integer serialisation; the API
  serialising an enum as int is conventional and is likely correct.
- Do not rename the SDK property or remove the enum values.
- Do not change the TypeScript SDK type shape without updating its contract
  tests in `packages/typescript/scout-sdk` too.
- Do not close this package while the raw-HTTP workaround remains in
  `SdkIntegrationE2ETests.cs`.

## Scope / files touched

- `src/KynticAI.Scout.Sdk` — the `ContextFactResult` model and its
  serialisation config (JSON converter registration)
- `tests/KynticAI.Scout.Sdk.Tests` — add/adjust contract tests
- `tests/KynticAI.Scout.EndToEndTests/SdkIntegrationE2ETests.cs` — replace the
  raw-HTTP workaround with typed SDK assertions
- `packages/typescript/scout-sdk/src/types.ts` and its tests — if the TS side
  has the same string-vs-enum mismatch, fix it in the same package
- `docs/sdk-development.md` and/or `docs-site` SDK pages — compatibility note
  if the .NET SDK value type visibly changes

## Tasks

1. **Confirm the wire format.** Read `src/KynticAI.Scout.Sdk` (ContextFactResult)
   and the API side (`FactValueType` in Domain/Models). Confirm the API emits
   the enum as an integer. Decide the SDK fix:
   - Recommended: type `ContextFactResult.ValueType` as the enum and register
     a JSON converter so the SDK accepts the API's integer encoding (and
     tolerates the existing string encoding if any producer sends it).
   - Alternative if backward-compat is critical: keep the property `string`
     and add a strongly-typed helper; but the audit prefers the typed enum.

2. **Fix the .NET SDK.** Implement the converter + typed property. Keep any
   `FactValueType` mapping aligned with the API enum values.

3. **Fix the tests.** Replace the raw-HTTP/JsonNode fallback in
   `SdkIntegrationE2ETests.cs` with typed SDK calls asserting `ValueType`
   round-trips correctly (e.g. `Number`, `Enum`, `String`). Keep a contract
   test that pins the integer wire value for each enum member so the two sides
   cannot silently drift again.

4. **Check the TypeScript SDK.** Read `packages/typescript/scout-sdk/src/types.ts`
   for `ContextFactResult.valueType`. If it mirrors the same string/int
   ambiguity, align it with the wire format and add/adjust its contract tests
   (`contract-types.test.ts` or similar). Run its suite.

5. **Compatibility note.** If the .NET SDK property type changes in a way
   that could break consumers, add a note in `docs/sdk-development.md` and a
   `[Unreleased]` line in `CHANGELOG.md` describing the corrected
   serialisation. The SDK user agent version may need a patch bump if a
   release follows.

## Acceptance criteria

- [ ] `ContextFactResult.ValueType` is typed correctly and round-trips in the
      .NET SDK tests and the end-to-end typed-SDK test.
- [ ] No raw-HTTP workaround remains in `SdkIntegrationE2ETests.cs`.
- [ ] A contract test pins the integer wire encoding of every `FactValueType`
      member on both API and SDK sides.
- [ ] TypeScript SDK type is consistent with the wire format and its tests
      pass.
- [ ] `.NET SDK` and `EndToEnd` suites pass; `packages/typescript/scout-sdk`
      `npm run build && npm test` pass.

## Verification

```powershell
dotnet test .\tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj
dotnet test .\tests\KynticAI.Scout.EndToEndTests\KynticAI.Scout.EndToEndTests.csproj

cd packages/typescript/scout-sdk
npm run build
npm test
```

## Notes

- This is the only known SDK defect in the repo, and it is currently masked
  by a test that deliberately avoids the broken path. Fixing it converts a
  hidden gap into verified behaviour.
- Coordinate with WP-009 (changelog) only if a release note is needed.
