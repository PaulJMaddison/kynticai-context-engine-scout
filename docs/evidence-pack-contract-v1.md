# UCL Evidence Pack Contract v1

Product framing note: `UclEvidencePackV1` is an existing implementation contract name. The canonical UCL product story is relationship sets and attribution paths first: exact data items are linked, journeys are ordered, comparable relationship sets can be analysed by private extensions, and governed JSON is handed to a customer-owned LLM, workflow, app, or agent for explanation.

`UclEvidencePackV1` is the stable local evidence-pack contract for Scout/UCL cross-repo use. It is emitted inside the customer-owned data plane and may contain exact authorised records, citations, provenance, relationships, `BasicRelationshipEngine` fallback weighted signals, recommendations, caveats, draft response content, and governance decisions.

`UclCloudAggregateUsageV1` is a separate Cloud/control-plane usage contract. It carries aggregate usage metadata only: package version, tenant/control-plane slug, feature/event/status, timestamps, feature counters, governance counters, and explicit data-boundary flags. It must not contain records, facts, snapshots, evidence packs, prompts, generated content, recommendations, citations, relationship types, weighted signals, caveats, hashed subject/account identifiers, or per-customer derived intelligence.

`UclEnterpriseRelationshipEngineHandoffV1` is a legacy-named proof-mode handoff contract for private relationship-set analysis. It is generated from local relationship/evidence JSON and stays in the customer-owned data plane. Scout does not call a live private service in open-core proof mode; it emits candidate relationships, citation provenance, fallback weight scope, and required private-extension outputs so a proof runner can apply advanced relationship-set analysis separately.

## Contract Artifacts

| Artifact | Path |
|---|---|
| .NET DTOs | `src/KynticAI.Scout.Application/Contracts/UclEvidencePackContracts.cs` |
| Local evidence-pack schema | `schema/ucl-evidence-pack-v1.schema.json` |
| Cloud aggregate usage schema | `schema/ucl-cloud-aggregate-usage-v1.schema.json` |
| Private-extension relationship handoff schema | `schema/ucl-enterprise-relationship-engine-handoff-v1.schema.json` |
| Control-plane boundary proof report | `docs/proof-artifacts/control-plane-boundary-proof/control-plane-boundary-proof.md` |
| Control-plane boundary proof script | `scripts/control-plane-boundary-proof.py` |
| Full local golden fixtures | `samples/evidence-pack/v1/local-full-evidence-packs.json` |
| Governed/masked golden fixtures | `samples/evidence-pack/v1/local-governed-masked-evidence-packs.json` |
| Cloud aggregate usage fixtures | `samples/evidence-pack/v1/cloud-aggregate-usage-payloads.json` |
| Unsafe Cloud payload examples | `samples/evidence-pack/v1/unsafe-cloud-payload-examples.json` |
| Private-extension weighting handoff sample | `samples/relationship-intelligence/enterprise-canonical-weighting-handoff.sample.json` |

## Enterprise Analysis Boundary

Scout may include `BasicRelationshipEngine` public fallback weights in the local `UclEvidencePackV1` payload so open-core tests and demos remain executable. Those weights are not canonical. The local pack includes `relationshipWeighting` metadata with `scope: "basic-public-fallback-only"` and `scoutWeightsAreCanonical: false`.

For proof mode, `EnterpriseRelationshipEngineHandoff` emits the legacy-named `UclEnterpriseRelationshipEngineHandoffV1` beside the local evidence pack. A private proof runner can consume that handoff, or the original `UclEvidencePackV1`, as the stable input for advanced relationship-set analysis, attribution paths, comparable examples, and outcome patterns. Private extensions must not require Cloud to receive local evidence packs or derived customer intelligence.

The handoff includes `requiresLiveEnterpriseService: false` and `enterpriseOnlyInternalsIncluded: false`. It must not contain private canonical formulae, scoring configuration, vector/index paths, embeddings, or private weighting internals.

Clarity and Importance are separate products and are not required by this contract.

## Fixture Coverage

The v1 golden fixtures cover B2B SaaS, ecommerce, support churn, recruitment, finance retention, and healthcare operations. Healthcare fixtures are operational and synthetic only; they do not include patient-level data.

Unit tests in `tests/KynticAI.Scout.UnitTests/UclEvidencePackContractTests.cs` validate the golden fixtures, citation/provenance integrity, masking behaviour, Cloud aggregate exclusions, private-extension handoff shape, and rejection of deliberately unsafe Cloud payload examples.

## Related Tooling

Operators who need to move context/evidence data out of a local Scout deployment can use the [KynticAI Scout Migration Tool](migration-tool.md), an open-core local-only export tool.
