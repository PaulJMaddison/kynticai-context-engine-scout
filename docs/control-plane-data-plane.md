# Customer Data and Optional Control Plane

Scout is designed so customer operational data can remain in the customer's environment.

## Scout runtime

The Scout runtime owns Scout-specific state such as:

- connector configuration and protected credential references;
- retained source-capture evidence where the customer has enabled it;
- mappings and selector definitions;
- materialised context facts and snapshots;
- relationship/evidence records produced by the public core;
- audit events, source events and background-job state;
- REST, GraphQL and SDK access.

Upstream CRM, ERP, warehouse, support, billing and other source systems remain the customer's systems. Scout does not become their system of record.

## Optional control plane

A separate hosted control plane may be used for commercial/operational metadata such as:

- account, licence and entitlement state;
- download/update metadata;
- support access;
- deployment registration, version and safe health metadata;
- explicitly approved aggregate usage counters.

It is optional for Scout open-source use.

## Data that stays local by default

The control plane must not receive these by default:

- connector credentials, private keys, tokens or connection strings;
- raw source records or retained exact payloads;
- context facts or snapshots;
- relationship/evidence packs or per-entity intelligence;
- prompts, generated customer content or recommendations;
- local databases, customer logs or customer-specific mappings.

## Product names

The product progression is **Scout → Fortress → Elite**.

Cloud/control-plane services are components that may support those products. They are not a replacement name for Elite.

## Scout to Fortress

Scout may prepare an explicit, customer-local upgrade handoff to Fortress using retained evidence, generation membership and durable ownership-transfer state.

That handoff remains customer data and must not be routed through the commercial control plane by default.
