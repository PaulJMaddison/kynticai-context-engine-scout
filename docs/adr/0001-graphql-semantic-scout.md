# ADR 0001: Use a GraphQL-based semantic data plane for AI-enabled sales workflows

## Status

Accepted

## Context

Intelligent Sales Support needs to turn fragmented operational data into grounded context that AI systems can safely consume. Raw source systems expose inconsistent shapes and identifiers:

- CRM records know contacts, stages, and plan interest
- product usage streams know engagement and activation behaviour
- SQL or warehouse tables know scored metrics and historical KPIs
- operators need explainable, tenant-safe, role-aware access across all of it

The AI layer also needs more than raw records. It needs a stable context package with:

- semantic attributes rather than source-specific columns
- provenance and freshness metadata
- confidence scoring
- fine-grained access control
- the ability to request only the slices a workflow needs

## Decision

We use a GraphQL-based semantic data plane, backed by a modular-monolith ASP.NET Core application, as the primary runtime surface between source data and AI-enabled sales workflows.

Selectors map raw signals into canonical attributes and materialize them into `ContextSnapshot` and `ContextFact` records. GraphQL becomes the contract for:

- admin workflows such as selector design, semantic registry management, audit access, and recompute control
- runtime workflows such as grounded user-context lookup and sales recommendation generation

## Why this is a strong fit

### 1. GraphQL matches semantic-context retrieval better than source-centric APIs

Sales workflows rarely want whole CRM or warehouse documents. They want a tailored context bundle such as:

- conversion probability
- preferred channel
- plan interest
- engagement level
- churn risk
- the evidence behind those judgments

GraphQL lets the client ask for exactly that semantic slice, without over-fetching source-specific payloads.

### 2. The semantic layer decouples AI consumers from raw-system churn

Source schemas change often. AI prompts should not. By materializing semantic attributes first, the model interface stays stable even when source connectors evolve.

### 3. Explainability is a first-class requirement

AI suggestions in sales need reviewability. `ContextFact` provenance, confidence, and freshness belong in the same contract as the semantic value itself. GraphQL makes those nested relationships natural to query and easy to surface in UI and agent tooling.

### 4. It supports both human and machine consumers with one contract

Admins, sales reps, and AI orchestration services all need overlapping but different views of the same context model. GraphQL supports role-aware field exposure without creating a separate endpoint per view.

### 5. It fits a modular monolith operationally

For the current product stage, the hardest problems are semantic correctness, lineage, and guardrails, not service decomposition. A GraphQL layer inside a modular monolith keeps selector execution, auth, audit, and AI orchestration in one deployable unit while preserving internal module boundaries.

## Consequences

### Positive

- stable, explicit context contract for AI workflows
- simpler frontend integration and admin tooling
- strong provenance and auditability story
- easier addition of future connectors without reworking the client contract
- better support for role-aware data shaping and masking

### Negative

- GraphQL schema discipline becomes important as the domain grows
- N+1 and resolver performance need active monitoring
- field-level authorisation and masking must be tested continuously

## Follow-up

- add persisted GraphQL operations for high-volume production clients
- add more explicit field-level policy metadata for sensitive attributes
- add connector-specific evaluation suites to measure semantic quality over time
