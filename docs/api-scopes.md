# API Scope Contract

Canonical API scopes use colon names:

- `context:read`
- `context:write`
- `selectors:read`
- `selectors:write`
- `events:ingest`
- `audit:read`
- `admin:manage`
- `blueprints:write`
- `billing:read`

`context:write` is an official scope for write operations that change context or request recomputation. SDK examples should request only the scopes needed by the workflow. Older dot-form scopes such as `context.read`, `context.write`, `events.write`, `audit.read`, `admin.manage`, `blueprints.write`, and `billing.read` are compatibility aliases only, as are `context.recompute` (maps to `context:write`) and `connectors.read` (maps to `context:read`).

## Machine Clients

Use named machine clients per integration rather than sharing one broad credential. Recommended scope groupings:

- context reader: `context:read`
- selector manager: `selectors:read`, `selectors:write`, `context:write`
- source event ingester: `events:ingest`
- audit export job: `audit:read`
- admin automation: `admin:manage` only when a human owner has approved the workflow

Tokens should be short-lived, tenant/workspace scoped, auditable, and rotated on a schedule. See `docs/machine-to-machine-identity.md` for the production M2M preparation checklist.
