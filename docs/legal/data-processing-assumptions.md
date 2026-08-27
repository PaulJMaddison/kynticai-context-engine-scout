# Data Processing Assumptions

This is a non-lawyer drafting aid for legal and data protection review. It is not legal advice. Validate these assumptions under UK GDPR, GDPR, PECR, customer contract terms, and the actual hosting/support model before production.

## Architecture Assumption

The customer data plane processes customer operational data locally by default. That includes connectors, source records, selectors, context facts, snapshots, provenance, audit logs, local API clients, and connector credentials.

The cloud control plane processes metadata needed to run a supported paid pilot: accounts, contacts, licences, downloads, update-channel metadata, support cases, optional aggregate usage, entitlement state, and commercial audit events.

## Raw Data Boundary

Raw operational data must not be sent to the cloud control plane or cloud support by default. This includes source rows, message bodies, documents, attachments, prompt packages, local logs containing payloads, connector credentials, database dumps, and private keys.

## Data Categories To Classify

- commercial account metadata
- product usage summaries
- support status summaries
- billing status summaries
- CRM-style opportunity or contact metadata
- operational workflow state
- identifiers and contact details
- special-category or highly sensitive data, if any

Message bodies, document bodies, attachments, detailed ticket descriptions, free-text notes, and analytics raw payloads are excluded unless explicitly approved.

## Roles And Responsibilities

Customer responsibilities should include lawful-basis review, source-field approval, masking rules, source credentials and rotation, customer environment backups, support-bundle approval, retention, and deletion expectations.

Supplier responsibilities should include processing only agreed pilot data, avoiding committed secrets/raw data, keeping provenance visible, documenting connector/selector/context behaviour, and reporting suspected incidents through the agreed route.

## First-Party Events

First-party event tracking requires a GDPR/PECR review before production. Each event must have a purpose, field list, retention period, consent/lawful-basis decision, and redaction rule. Avoid free-text fields and customer operational identifiers.

## Support Handling

Support bundles should be redacted locally before sharing. If a customer asks for deeper support, agree the minimum dataset, secure transfer route, retention, access controls, and deletion evidence before transfer.

## Backups, Export, And Delete

The customer should own backup and restore for the customer data plane unless the SOW says otherwise. Restore rehearsals should include the Scout database, retained Scout evidence, capture checkpoints/ownership state, connector configuration, and ASP.NET Data Protection keys where protected credentials depend on them. Upstream CRM/ERP/support/warehouse systems remain under the customer's existing backup and recovery ownership; retained Scout evidence is not a full backup of an upstream source unless the connector contract explicitly guarantees that.

Offboarding should define export format, deletion or return of support bundles, licence expiry or revocation, credential revocation, temporary access removal, and retained audit records.

## Open Questions For Legal Review

- controller/processor split for paid pilot operations
- whether aggregate usage counts can identify a customer or user
- retention periods for leads, support, audit, and download logs
- international transfer and subprocessor list
- data subject request handling route
- incident notification wording and timing

## Mandatory Legal Review Notes

- This draft is not legal advice.
- Customer data-plane operation keeps raw operational data, connector credentials, selectors, facts, snapshots, and provenance local by default.
- Cloud control-plane processing is limited to metadata unless separately reviewed.
- Support data must be redacted; raw operational data must not be sent to cloud support by default.
- First-party event tracking needs notice, consent/lawful-basis assessment, minimisation, retention, and opt-out/withdrawal handling.
- GDPR, UK GDPR, and PECR review is required before production.
