![KynticAI](https://github.com/PaulJMaddison/kynticai-context-engine-scout/blob/main/docs/images/brand/kynticai-readme-logo.jpg?raw=true)

# KynticAI Scout — Retired / Historical

> **Status: retired.** Scout is no longer an active KynticAI product and this repository is no longer part of the current KynticAI production product suite.
>
> This repository is preserved as a historical, open-source proof of concept showing an earlier KynticAI approach to customer-controlled data ingestion, semantic/context construction, relationship linking, provenance and API delivery. It is **not** the current KynticAI production architecture.

Scout was an important step in the evolution of KynticAI. It explored how fragmented business data could be brought together, linked, attributed to source evidence and exposed as useful context for software and AI systems.

The repository remains public because the engineering ideas are useful in their own right and because it provides a clear historical record of how the platform evolved.

## What this repository represents

Scout demonstrates an earlier public context/data-plane approach, including concepts such as:

- connecting authorised business data from multiple sources;
- normalising source records into reusable fields and facts;
- linking records that refer to the same business entities;
- retaining provenance and source evidence;
- exposing constructed context through APIs and SDKs;
- local/self-hosted deployment patterns;
- public connector and discovery tooling.

Those ideas influenced later KynticAI work, but the code in this repository should be read as a **historical proof and reference implementation**, not as the architecture of the current commercial platform.

## What this repository does not represent

This repository does **not** contain the current KynticAI production system, the finished KynticAI architecture, or the private commercial implementation used by current products.

In particular, nothing in this repository should be treated as a complete description of current KynticAI reasoning, evidence construction, governance, intervention/execution, outcome modelling, outcome memory, compounding intelligence, Fortress internals, Elite integration, or other private production capabilities.

The historical Scout/Fortress/Elite progression described in older documentation is no longer the current product structure.

## Project status

Scout is frozen.

- No new product features are planned.
- No new commercial pilots should be routed through Scout.
- Scout should not be positioned as an entry tier for Fortress or Elite.
- Historical deployment, migration, buyer, pilot and roadmap documents remain in the repository for reference only unless explicitly marked otherwise.
- Existing code remains available under its original open-source licence.

See [RETIRED.md](RETIRED.md) for the canonical retirement note.

## Using the repository

You are welcome to inspect, fork and experiment with the code under the terms of the MIT licence. Because the project is retired, you should not assume ongoing maintenance, production support, security updates, compatibility guarantees or a migration path into current KynticAI products.

Historical setup and implementation documentation remains under [`docs/`](docs/).

## Current KynticAI

Current KynticAI products and architecture are developed separately from this repository.

For current information, visit [kynticai.com](https://kynticai.com).

## Licence

KynticAI Scout remains available under the [MIT License](LICENSE).
