---
title: n8n Node
description: Local KynticAI Scout n8n node for source-system event ingestion.
---

The local n8n package lives in `packages/typescript/n8n-node`.

It is a write-only node for sending provider-neutral source-system events to:

```http
POST /api/v1/events/source-system?tenantSlug=<tenant>
```

Status: local package readiness is complete. npm publication and n8n
marketplace submission are not yet available; use the local package path
shown below. See `docs/connector-marketplace.md` in the repo for the
public connector catalogue and readiness labels.

## Local Checks

```bash
cd packages/typescript/n8n-node
npm install
npm run build
npm test
npm run validate:local
```

`validate:local` runs the local build, focused tests, and `npm pack --dry-run`.
It does not publish to npm.

## Credentials

- `baseUrl`
- `apiClientId`
- `apiKey`

The API client should have the `events:ingest` scope.

## Mapping

Each input item maps to one Scout event. Configure the tenant, source system,
event type, optional workspace slug, event ID field, external user ID field,
external account ID field, and observed-at field.

## Validation And Redaction

The node validates credentials and mapped fields before sending an item.
Base URLs must be HTTP or HTTPS URLs without embedded credentials, query
strings, or fragments. Tenant and workspace slugs are normalised locally.
Input items must be JSON-safe objects.

Payload keys that look like credentials or secrets, including API keys, tokens,
passwords, cookies, signatures, and private keys, are recursively replaced with
`[REDACTED]`. Node error messages report status/code hints only and avoid
echoing request headers, API keys, or payload fragments.

Deterministic package fixtures live in `packages/typescript/n8n-node/fixtures`.

Publication, marketplace submission, release automation, tags, and workflow
changes are outside this local package slice.
