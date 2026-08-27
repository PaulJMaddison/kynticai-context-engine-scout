# Scout Sales Reference Example

This project contains the fictional sales/next-action and sales-prompt packaging logic that was previously mixed into Scout core.

It is deliberately a **reference example**, not Scout core behaviour.

The example shows how a consumer can:

- work with fictional CustomerOps-style records;
- build deterministic relationship/evidence links;
- apply example sales/retention weights;
- create a next-action recommendation;
- build a sales-specific context package and prompt envelope for an external model consumer;
- produce a public-safe handoff contract for richer private analysis.

The numeric weights and recommendation rules in this project are demonstration business rules. They are not calibrated KynticAI platform intelligence and are not Fortress/Elite algorithms.

Production Scout does not register these implementations. The core `INextActionIntelligenceService` binding is the disabled compatibility implementation, and the legacy sales-context contract is backed by a neutral context packager that has no fixed sales-required attributes, weights, recommendation rules, prompt orchestration, or model execution.

Tests may reference this project directly to prove the example remains executable without making it a runtime dependency of the Scout core.
