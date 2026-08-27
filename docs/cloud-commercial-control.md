# Optional Commercial Control Plane Contract

This document describes the public Scout-side contract for an optional hosted commercial control plane.

It is an architectural component, **not a fourth product and not the third product instead of Elite**. The current product progression is **Scout → Fortress → Elite**.

## Purpose

The control plane may manage:

- commercial account metadata;
- licence and entitlement state;
- private download/update metadata;
- support access and support-case metadata;
- deployment registration, version and safe health status;
- explicitly approved aggregate usage counters.

Scout remains usable without it.

## Strict data boundary

The control plane must not receive by default:

- raw customer operational data;
- source credentials;
- retained exact source evidence;
- context facts/snapshots;
- relationship sets, weights or per-customer derived intelligence;
- prompts or generated customer content;
- customer-specific connector mappings;
- private deployment secrets.

If future support activity requires customer data, that is a separate explicit customer-approved support process, not normal control-plane telemetry.

## Upgrade and product boundary

- **Scout — Explore:** public open-source product.
- **Fortress — Prove:** private production product.
- **Elite — Scale:** enterprise scale product.

The control plane may support commercial/licensing/update workflows around these products, but it is not itself the customer data plane.

## Scout configuration

Public Scout may expose provider-neutral control-plane settings for optional licence/update/support integration. Enabling a runtime deployment mode must not silently enable commercial-control features; those settings are explicit.

## Aggregate usage

Where customers opt in, only allowlisted aggregate counters and deployment metadata may be sent. Do not include entity identifiers, relationship types, citations, prompts, recommendations or raw/derived customer intelligence.
