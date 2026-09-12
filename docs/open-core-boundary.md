# Open-Core Boundary — Historical

> **Superseded:** Scout is retired. This document no longer defines the public/private boundary of the current KynticAI platform. See [`../RETIRED.md`](../RETIRED.md).

Scout was originally developed as an open-core product with public extension contracts and private commercial implementations.

That architecture and product strategy are historical.

## What this repository now means

The entire Scout repository should be treated as a preserved public proof of concept. Its code and documentation explain Scout itself, not the current KynticAI product architecture.

The following rules now apply:

- do not add new current KynticAI production capabilities here;
- do not use this repository to document current private architecture or product boundaries;
- do not add customer-specific, security-sensitive or commercial implementation material;
- do not infer current KynticAI internals from old Scout extension points or documents;
- keep current production IP in the private repositories where it belongs.

## Historical value

The public code remains useful as a reference for earlier work around semantic/context construction, relationship linking, provenance, APIs, SDKs, connectors and customer-controlled deployment.

Older commits preserve the detailed open-core strategy that existed while Scout was an active product. That strategy is no longer authoritative.
