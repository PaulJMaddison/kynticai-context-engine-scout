# Commercial Readiness Summary

Workspace naming source of truth: [`source-of-truth-naming-map.md`](source-of-truth-naming-map.md).

KynticAI Scout is ready to show publicly and ready for paid pilot conversations. It is nearly ready for paid pilot delivery after live hosting configuration, legal review, and first-customer operational setup. It is not ready for complete self-serve SaaS and must not be sold that way.

## Ready To Show Publicly

- product narrative: "We do not build the brain. We build the nervous system."
- open-core local demo and customer data-plane story
- public docs/demo app, local demo, and admin console
- customer data-plane boundary
- REST/GraphQL/SDK foundations
- supported paid pilot positioning

## Ready For Paid Pilot Conversations

- supported pilot offer
- customer data-plane install runbooks
- production preflight scripts
- legal/privacy draft pack for review
- connector proof pack in the private enterprise repo
- cloud control-plane operations docs in the private cloud repo

## Nearly Ready For Paid Pilot Delivery

Remaining before live hosting:

- select reviewed branch/tag for public and cloud hosting
- configure real hosting, domains, DNS, TLS, CORS, secrets, and backup schedule
- complete legal review of privacy, cookie/event consent, terms, pilot agreement, and data processing assumptions
- run restore rehearsal and record evidence
- confirm support owner, incident owner, and response targets
- configure real SMTP or explicitly disable notifications
- configure production licence keys in a secret store

Remaining before first customer install:

- agree customer source system, owner, network route, read-only access, and fields in/out of scope
- create restricted views or read replica
- validate SQL/PostgreSQL connector dry-run against customer-approved data
- issue customer licence and package entitlement
- register customer data plane with aggregate-only metadata
- run backup, restore, health, audit, connector, selector, and support-bundle smoke checks

## Claims Not To Make Yet

- complete self-serve SaaS
- vendor-certified connector suite
- Do not claim vendor-certified connectors.
- live hosted billing is ready
- SLA-backed support without a signed support process
- cloud control plane stores or needs raw customer operational data
- paid enterprise implementation code is in the public repo
- real customer IdP support is validated before a customer IdP proof
- customer traction, customer proof, or customer deployment without real dated evidence such as replies, meetings, LOIs, pilots, revenue, signed acceptance, or equivalent records

## Best Sales Wording

"KynticAI Scout helps teams turn existing operational data into trusted context for their own AI tools, workflows, apps, reports, and agents. The first offer is a supported paid pilot that keeps customer operational data in the customer data plane by default."

## Best CTO Wording

"Scout is a governed semantic data plane beside your existing systems. It exposes context through REST, GraphQL, SDKs, and events, with provenance, freshness, masking, audit, scoped machine clients, and a clear control-plane metadata boundary."

## Best CEO Wording

"You do not need to replace your systems or bet the company on a new AI app. Scout makes the data you already have usable, governed, and reusable across the AI and workflow tools you choose."

## Completed Before Live Hosting

- public lead capture copy links to privacy/terms drafts and warns against raw operational data
- cloud portal same-origin topology, security headers, CORS policy, auth caveats, build/env guidance, and migration scripts are documented
- enterprise disposable PostgreSQL proof and private package dry-run scripts are ready locally
- cross-repo paid-pilot rehearsal, M2M/webhook smoke, and licence-install rehearsal scripts are present

## Still Requires Live Environment Next Week

- real hosting platform, domains, DNS, TLS, reverse proxy, and HSTS validation
- managed PostgreSQL, backup schedule, restore evidence, and monitoring
- production secret-store references for JWT, licence signing, SMTP, billing, and lead challenges
- exact CORS/CSP values for the chosen topology
- legal review of privacy, cookie/event consent, terms, paid pilot agreement, and data processing assumptions

## Still Requires First Customer Pilot

- customer-approved source system, owner, fields in/out of scope, read-only route, and credential vault reference
- connector dry-run and preview against customer-approved data
- package entitlement, licence install, data-plane registration, support workflow, and backup/restore evidence for that customer
- signed support/data handling process for any exceptional diagnostic transfer

## Still Not Ready To Promise

- complete self-serve SaaS
- vendor-certified connectors
- hands-off live billing and automated entitlement fulfilment
- managed customer data-plane hosting
- cloud storage of raw customer operational data by default
- SLA-backed support without a signed process and named rota

## Public Marketing Launch Go/No-Go

- public docs/demo app build passes
- privacy and terms draft links are visible
- paid-pilot wording avoids complete SaaS and vendor-certified connector claims
- no private enterprise code or secrets are present in public artefacts
- production environment check passes for the selected host

## Paid Pilot Delivery Go/No-Go

- cloud live-hosting preflight passes
- backup and restore rehearsal evidence exists
- licence signing keys and JWT secrets are in a secret store
- customer connector proof pack is approved
- support owner, incident owner, and redaction checklist are agreed
- licence install and data-plane registration are rehearsed without raw operational data leaving the customer data plane
