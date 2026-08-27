# Scout Roadmap

This roadmap covers the public KynticAI Scout product.

Canonical product names are defined in [source-of-truth-naming-map.md](source-of-truth-naming-map.md).

## Product boundary

- **Scout — Explore:** this open-source repository.
- **Fortress — Prove:** private product outside this repository.
- **Elite — Scale:** enterprise scale product outside this repository.
- Optional Cloud/control-plane services are supporting infrastructure, not the third product.

## Shipped public foundations

Scout currently includes:

- source connectors and connector extension contracts;
- retained source evidence/continuity foundations;
- mappings/selectors;
- context facts and snapshots;
- relationship/evidence foundations;
- REST and GraphQL APIs;
- TypeScript and .NET SDKs;
- local/demo and PostgreSQL deployment paths;
- authentication, API clients, audit and webhook/event ingestion;
- admin/demo web application;
- generic Discovery Agent;
- metadata-only Scout Discovery MCP;
- public connector authoring/validation tooling;
- Scout-to-Fortress customer-local continuity tooling.

KynticAI Score is a separate companion product. A public Score contract/client may live alongside Scout temporarily, but it is not a Scout scoring engine capability.

## Current architecture programme

The 2026-08-27 architecture review is tracked in [work-packages/README.md](work-packages/README.md).

Priorities are:

1. make Scout/Fortress/Elite and Discovery ownership consistent everywhere;
2. make the Scout core inference-free in runtime behaviour;
3. remove the fictional CustomerOps database and sales heuristics from mandatory production architecture;
4. separate deployment mode from optional control-plane/commercial flags;
5. make workspace semantics truthful;
6. prove hosted key persistence and atomic cross-instance event idempotency;
7. simplify repository/API boundaries after those architectural changes.

## Directional priorities

After the architecture programme:

- improve connector quality and authoring experience;
- strengthen source-evidence and replay guarantees;
- improve API/SDK consistency;
- keep self-hosting simple;
- improve documentation and executable examples;
- preserve stable public extension contracts without leaking private implementation.

## Principles

- Scout must remain useful without paid/private products.
- Customer operational data stays customer-controlled by default.
- Examples must not masquerade as platform truth.
- Public interfaces may expose extension seams; private implementations stay private.
- Current documentation must describe shipped reality rather than aspirational state.
