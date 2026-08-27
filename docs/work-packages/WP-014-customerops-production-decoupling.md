# WP-014 — Decouple CustomerOps demo data from the production Scout runtime

## Metadata

- **Status:** Complete
- **Priority:** Critical
- **Phase:** E — Architectural truth and product boundary
- **Depends on:** WP-012
- **Review gate:** xhigh (data architecture, persistence, deployment)

## Context

Scout's product promise is to sit beside existing customer systems and access approved data through connectors.

The repository also contains a `CustomerOpsDbContext` with a sales/SaaS-shaped operational model: accounts, contacts, opportunities, activities, support tickets, product usage, billing metrics, web conversion events, subscriptions and plans.

That model is useful fictional/reference data. The problem is that production currently treats it as mandatory:

- production readiness requires both Scout and CustomerOps connection strings;
- `/health` and `/health/ready` require both databases;
- `render.yaml` provisions both managed PostgreSQL databases;
- deployment/backup docs instruct operators to manage both.

This changes the implied production architecture from "customer systems → Scout connectors" into "customer systems → a Scout-owned CustomerOps schema → Scout".

## Objective

Make `CustomerOps` a demo/reference-application concern rather than a mandatory Scout production dependency.

A production Scout data plane must be healthy with its own state store and configured connectors, without a second Scout-owned copy of customer operational data.

## Target architecture

```text
Demo/reference:
fictional CustomerOps DB -> reference connectors/use case -> Scout

Production:
customer systems -> approved Scout connectors -> Scout state/evidence DB
```

## Tasks

1. Inventory every dependency on `ICustomerOpsDbContext` and classify it as core, demo, reference application or test fixture.
2. Remove CustomerOps from core production readiness and generic health checks.
3. Move CustomerOps domain/persistence/seeding into a clear example/reference boundary such as `samples`, `examples` or a dedicated reference application.
4. Ensure core services do not directly query CustomerOps tables for generic Scout behaviour.
5. Update Docker/Render/production configuration so a real Scout deployment does not require `ConnectionStrings:CustomerOps`.
6. Keep a simple opt-in demo composition that still starts fictional CustomerOps data for the sales/customer-360 example.
7. Rewrite backup/restore guidance so Scout owns Scout state/evidence/key material, while upstream source-system backup remains the customer's existing responsibility.
8. Adjust tests: demo/reference tests may use CustomerOps; core production-readiness tests must prove it is optional.
9. Add a migration/compatibility note for existing demo installations.
10. Verify no data-loss path is introduced for existing Scout databases.

## Do not do

- Do not delete useful fictional demo data.
- Do not copy CustomerOps tables into the Scout DB.
- Do not replace the second DB with another mandatory intermediate operational schema.
- Do not weaken connector/source evidence capture.
- Do not make health checks ignore genuinely required Scout dependencies.

## Acceptance criteria

- [ ] Production Scout starts and reports ready with no CustomerOps database configured.
- [ ] Generic Scout context behaviour consumes connector/captured data rather than direct CustomerOps queries.
- [ ] CustomerOps remains available as an explicitly labelled reference/demo application if useful.
- [ ] Render/hosted examples do not provision a mandatory CustomerOps production database.
- [ ] Production backup docs no longer claim ownership of upstream customer systems.
- [ ] Tests distinguish core runtime from reference/demo runtime.

## Verification

Include PostgreSQL startup/readiness proof for a single Scout database plus configured test connector path. Run the disposable GCP exact-SHA gate for final persistence/deployment proof.
