# Scout Sales Reference Example

This project contains the fictional sales/next-action logic that was previously mixed into the Scout application layer.

It is deliberately a **reference example**, not Scout core behaviour.

The example shows how a consumer can:

- work with fictional CustomerOps-style records;
- build deterministic relationship/evidence links;
- apply example sales/retention weights;
- create a next-action recommendation;
- produce a public-safe handoff contract for richer private analysis.

The numeric weights and recommendation rules in this project are demonstration business rules. They are not calibrated KynticAI platform intelligence and are not Fortress/Elite algorithms.

Production Scout does not register this implementation. The core `INextActionIntelligenceService` binding is the disabled compatibility implementation, so the legacy next-action API fails explicitly instead of running sales scoring inside Scout.

Tests may reference this project directly to prove the example remains executable without making it a runtime dependency of the Scout core.
