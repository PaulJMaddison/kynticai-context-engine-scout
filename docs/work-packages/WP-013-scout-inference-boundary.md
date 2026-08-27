# WP-013 — Make the Scout inference boundary real

## Metadata

- **Status:** Complete
- **Priority:** Critical
- **Phase:** E — Architectural truth and product boundary
- **Depends on:** WP-012
- **Review gate:** xhigh (architecture, data boundary, public API)

## Context

Scout's canonical public positioning says Scout prepares trusted context and **does not call an AI model**. That is a strong architecture boundary: Scout should make source-traced context available to customer-owned apps, workflows, agents and model runtimes.

The current code nevertheless contains model-execution infrastructure, including:

- `LlmOptions`
- `IStructuredLlmClient` / registry
- `SalesSupportAgentService.GenerateAsync`
- prompt construction and structured model requests
- a public API/demo path capable of invoking the configured mock/provider-backed structured LLM client

The default provider being mock does not remove the architectural contradiction: the core is explicitly designed to execute an inference provider.

## Objective

Make the public claim literally true:

> Scout produces governed context; Scout core does not execute an AI model.

Preserve the useful sales-agent demonstration by moving model execution to a clearly labelled reference consumer/example outside the Scout core runtime.

## Design intent

The boundary should become:

```text
source systems
  -> Scout capture/mapping/context APIs
  -> governed context package
  -> external/reference consumer
  -> optional model chosen by the customer
```

Scout may define stable output contracts that AI consumers use. It should not own provider credentials, model selection, prompt execution or inference retries in its core runtime.

## Scope

Inspect at minimum:

- `src/KynticAI.Scout.Infrastructure/AI/**`
- `IStructuredLlmClient*` abstractions
- `ISalesSupportAgentService`
- agent-run GraphQL/REST paths
- prompt template/domain entities if they exist only for inference
- `apps/web` agent playground
- tests covering agent/model execution
- docs/API examples claiming provider-backed execution
- configuration `Llm:*`

## Tasks

1. Trace every runtime path that can invoke an LLM abstraction.
2. Separate **context packaging** from **model execution**.
3. Keep context-package generation in Scout where it is generic and useful.
4. Move model invocation, prompt orchestration and provider configuration into a reference application/example package, or remove it where it is demo-only duplication.
5. Ensure the reference consumer obtains context only through supported public APIs/SDKs rather than privileged internal access.
6. Remove model-provider settings from Scout production configuration if no longer required.
7. Ensure no customer context can accidentally leave Scout through a model provider because of a Scout runtime setting.
8. Preserve useful UI demonstrations by making them visibly external/reference behaviour.
9. Update API contracts, SDKs and docs carefully; add compatibility notes for any removed/deprecated endpoints.
10. Add tests proving Scout core can build/serve context without any inference provider registration and that no core request initiates inference.

## Do not do

- Do not remove the useful governed context package.
- Do not move private Fortress implementation into the public repo.
- Do not replace the LLM path with a hidden HTTP call.
- Do not retain misleading "provider-backed" wording if the provider is now an example consumer.
- Do not break public clients silently; deprecate or version where needed.

## Acceptance criteria

- [ ] Scout core contains no production path that executes an AI model.
- [ ] Scout can run with no model/provider configuration.
- [ ] The sales/agent demonstration still works as a reference consumer if retained.
- [ ] Model credentials cannot be configured on the Scout core runtime.
- [ ] Public docs truthfully state where inference happens.
- [ ] Context-package contracts remain usable by arbitrary customer models/workflows.
- [ ] Regression tests prove the boundary.

## Verification

Use focused tests for context-package generation and API compatibility, then the full repository validation gate. Any public contract removal must include migration/deprecation evidence.
