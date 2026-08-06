# WP-011 — Connector authoring and marketplace documentation (OSS-019)

## Metadata

- **Status:** Complete
- **Priority:** Medium
- **Phase:** D — Documentation completeness
- **Depends on:** —
- **Review gate:** standard (xhigh if a connector contract example changes)

## Completion notes

- `docs/connector-plugin-model.md` verified against code: added `mockCrm`,
  `mockBilling`, `mockSupport`, the `postgresql` alias, full alias/kinds per
  plugin, and `ConnectorPluginBase` default capabilities with demo/inventory
  overrides.
- New `docs/connector-authoring-tutorial.md`: end-to-end walkthrough
  (scaffold → contract rules → GraphQL/REST register → validate + health →
  selector-preview provenance → marketplace), every step runnable with the
  safe default. The template's lack of a `.csproj` is documented honestly
  (coverage = unit tests + byte-for-byte diff, confirmed zero-diff).
- `docs/connector-authoring.md` cross-linked to the tutorial; the AcmeCRM
  example now derives provenance from the response's `observedAtUtc`.
- `docs/connector-test-harness.md`: fixed a broken CLI command (was crashing
  with a TypeError); now runs against the template manifest and documents the
  `--records` array requirement (verified 21/21).
- `docs/connector-manifest-validator.md`: added the template validation
  command (verified "All valid").
- `docs/connector-marketplace.md`: explicit statement that placeholder
  catalogue entries are metadata only (no execution, no vendor
  certification), matching the web console readiness labels; tutorial link.
- docs-site `connectors/authoring.md` and `concepts/connector-basics.md`
  aligned (fixed a stale "three built-in plugins" table to all nine) and
  cross-linked; docs-site rebuild passes (22 pages).
- Verification: ConnectorAuthoringTests + ConnectorPluginModelTests 27/27;
  harness CLI 21/21; validator CLI "All valid"; docs-site build clean;
  `git diff --check` clean; no new connector code/catalogue/contract changes.

## Context

AGENTS.md lists **OSS-019** ("connector authoring remain upcoming public-
facing work"). The audit found the connector story is functionally complete
in code but the documentation has gaps and inconsistencies:

- Real model: 8 registered plugins (`mock`, `restApi`, `sql`, `csvUpload`,
  `mockCrm`, `mockBilling`, `mockSupport`, `inMemoryInventory`, `template`) in
  `src/KynticAI.Scout.Infrastructure/Connectors`; `ConnectorRegistry`;
  `ProtectedConnectorCredentialStore` (`secret://` refs + DataProtection);
  `ConnectorMetadataValidator`; `SelectorExecutionEngine` provenance; GraphQL
  ops `connectorPlugins`, `registerConnector`, `validateConnectorConfiguration`,
  `checkConnectorHealth`; the `samples/connector-template` mirrors the runtime
  `TemplateConnectorPlugin` byte-for-byte.
- Catalogue: `ConnectorCatalogueSeeder` marks private connectors as safe
  placeholders ("Unavailable in open source; safe metadata only").
- Docs that exist: `docs/connector-plugin-model.md`,
  `docs/connector-authoring.md`, `docs/connector-marketplace.md`,
  `docs/connector-manifest-validator.md`, `docs/connector-test-harness.md`,
  docs-site `connectors/authoring.md` and `concepts/connector-basics.md`,
  plus `samples/connector-template/README.md`.

Gaps identified:
1. No single end-to-end connector authoring tutorial that walks a user from
   scaffold (`samples/connector-template`) → register via GraphQL →
   validate configuration → fetch → observe provenance in a selector result.
2. `docs/connector-authoring.md` may be inconsistent with the current
   contract rules (e.g. `secret://` enforcement in samples, deterministic
   provenance, `ConnectorContractRules.ValidateIngestEvent`, no external AI
   models) — needs a verification pass against the code.
3. The marketplace doc and the web console's `connector-readiness.ts` labels
   ("Executable open-core" / "Mock/local proof" / "Private/customer-specific"
   / "Placeholder" / "Not vendor-certified") must agree, and the doc must not
   imply vendor certification for placeholder catalogue entries.
4. docs-site connector pages should be cross-linked with `samples/connector-
   template` and the repo docs.

## Objective

Turn the existing connector documentation into a complete, verified,
consistent authoring and marketplace story: one tutorial, one contract
reference, and one marketplace truth that match the code.

## Do not do

- Do not add new connector implementations, plugins, or catalogue entries in
  this package (documentation only).
- Do not claim vendor certification or enterprise connector availability in
  the open core.
- Do not change the connector plugin contract or the `secret://` rule.
- Do not copy private/enterprise connector code into samples.

## Scope / files touched

- `docs/connector-authoring.md` (verify and extend)
- `docs/connector-plugin-model.md` (verify table of plugins/aliases)
- `docs/connector-marketplace.md` (verify against `connector-readiness.ts`
  labels; coordinate with WP-008's copy review)
- `docs/connector-manifest-validator.md` and `docs/connector-test-harness.md`
  (verify commands against `samples/connector-template`)
- docs-site `src/content/docs/connectors/authoring.md` and
  `src/content/docs/concepts/connector-basics.md` (align + cross-link)
- `samples/connector-template/README.md` (only if the tutorial uncovers an
  inconsistency)
- Possibly a new `docs/connector-authoring-tutorial.md` if the walkthrough
  does not fit existing files

## Tasks

1. **Verify the contract rules against code.** Read
   `docs/connector-plugin-model.md` and confirm every plugin/alias/capability
   it documents matches `src/KynticAI.Scout.Infrastructure/Connectors`
   (aliases like `restApi`→`apiPayload/crmApi/billingApi/telemetryApi/
   productTelemetry/supportApi`, `mock`→`mockPayload/mockSignal/fileUpload`)
   and `ConnectorPluginBase` default capabilities. Fix any drift in the doc.

2. **Write the end-to-end tutorial.** Recommended structure for
   `docs/connector-authoring.md` (or a new tutorial page):
   - Scaffold: copy `samples/connector-template`, build it, run its tests.
   - Contract rules recap: deterministic provenance in `ConnectorFetchResult`,
     `secret://` credential references (validated by `ConnectorMetadataValidator`),
     `ConnectorContractRules.ValidateIngestEvent`, no external AI models, no
     local persistence of customer data beyond the contract.
   - Register: GraphQL `registerConnector` mutation and REST/API-client path,
     with the required manifest fields.
   - Validate + health: `validateConnectorConfiguration` and
     `checkConnectorHealth`, plus the validator CLI if documented in
     `docs/connector-manifest-validator.md`.
   - Use: create a DataSource, fetch, then observe provenance in a
     `SelectorExecutionEngine` result (selector preview).
   - Marketplace: how the catalogue seeder marks entries (open-core vs
     placeholder) and how the web console labels them.
   - Each step must be runnable with the safe default (no Docker, no external
     services). If a step needs Docker, gate it like the existing opt-in
     proofs.

3. **Align marketplace truth.** Ensure `docs/connector-marketplace.md` uses
   the same readiness labels as `apps/web/src/.../connector-readiness.ts` and
   states explicitly that placeholder catalogue entries are metadata only
   (no execution, no vendor certification). Coordinate with WP-008 Task 4 so
   the two packages do not conflict; if WP-008 lands first, reuse its wording.

4. **Align docs-site pages.** Update docs-site `connectors/authoring.md` and
   `concepts/connector-basics.md` to match the tutorial and the marketplace
   labels, and add cross-links to `samples/connector-template` and the repo
   connector docs. Rebuild docs-site.

5. **Add a contract test if the docs rely on one.** If the tutorial promises
   that `samples/connector-template` passes validation, make sure the existing
   unit tests (e.g. `ConnectorAuthoringTests` in `tests/KynticAI.Scout.UnitTests`)
   still cover that and cite them in the doc.

## Acceptance criteria

- [ ] Every plugin/alias/capability in `docs/connector-plugin-model.md`
      matches the code.
- [ ] A complete, runnable connector authoring tutorial exists and passes with
      the safe default.
- [ ] `docs/connector-marketplace.md` matches the web console readiness
      labels and explicitly excludes vendor certification for placeholders.
- [ ] docs-site connector pages align with the tutorial and rebuild cleanly.
- [ ] No new connector code, catalogue entries, or contract changes were
      introduced.
- [ ] `docs/connector-test-harness.md` commands run against
      `samples/connector-template`.

## Verification

```powershell
# Connector authoring unit tests (documented as the tutorial's check)
dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj --filter "FullyQualifiedName~ConnectorAuthoringTests|FullyQualifiedName~ConnectorPluginModelTests"

# Template builds and tests
dotnet build .\samples\connector-template\...
dotnet test  .\samples\connector-template\...

# Docs site rebuild
cd docs-site
npm install
npm run build
```

## Notes

- This is the final OSS-019 public-facing item; after it lands, the connector
  story is complete and auditable end-to-end.
- Keep the tutorial deterministic and local; any opt-in step must be labelled
  with the same gate variables used elsewhere.
