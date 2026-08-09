# Product Positioning

Workspace naming source of truth: [`source-of-truth-naming-map.md`](source-of-truth-naming-map.md).

Context Engine · Scout turns authorised company data items into relationship sets, attribution paths, comparable examples, outcomes, and governed JSON for approved customer-owned consumers. Use UCL for repo history, architecture lineage, and stable implementation contract names.

Scout is the free/open-source local proof path for the Context Engine. It ingests exact data items through connectors or one-off assisted imports, stores them in the customer-owned data plane, and gives customer systems a governed way to read those items, relationships, provenance, audit, and basic fallback intelligence.

The flagship Context Engine · Scout workflow is: source systems -> UCL/Scout customer-owned data plane -> exact data items, relationships, attribution paths, comparable relationship sets, and outcomes -> governed JSON with evidence, matches, ranked options, confidence, and caveats -> customer-owned apps, workflows, local LLMs, or agents.

The buyer message is simple:

- customers keep CRM, ERP, support desk, billing, warehouse, product databases, spreadsheets, and legacy systems
- Scout sits beside those systems and maps authorised signals into exact data items, local relationships, attribution paths, outcomes, provenance, governance, and basic fallback intelligence
- data items can include customers, email addresses, browser cookies, web events, email enquiries, product views, support events, registrations, purchases, accounts, opportunities, invoices, or abstract events such as "generic web search" and "email enquiry"
- Fortress/private extensions can compare relationship sets and return governed JSON with comparable examples, attribution-path evidence, ranked action options, confidence, and caveats
- customers can bring their own LLM, workflow engine, report, or app for the text explanation
- the customer-owned data plane keeps operational data customer-controlled by default
- the public repo contains the open core, local demo, admin console, APIs, SDKs, extension seams, generic connectors, and mock/demo connectors
- Fortress/private modules can provide advanced relationship-set analysis, attribution-path analysis, scoped private connector modules, SSO/SAML, SCIM, vault integrations, advanced governance, compliance exports, deployment packs, and support tooling
- paid/private Cloud modules provide optional hosted account management, billing, commercial licence portal, download portal, update channels, support portal, health/status, aggregate usage reporting backend, and cloud operations only
- metadata-only remains a safe discovery mode for mapping systems and governance; private exact-data mode is the core product promise for production relationship-set analysis
- KynticAI Discovery MCP is the buyer-facing metadata-only wrapper for IT-manager discovery: local codebase audit, public connector catalogue inspection, manifest validation, metadata quality report, Discovery Signature review, and optional signature-only handoff for a KynticAI-built synthetic demo
- Clarity and Importance are separate KynticAI products, not required UCL dependencies

Avoid overclaiming. The public repo is a credible open-core product foundation, not a self-serve hosted SaaS product, not a paid enterprise connector pack, not vendor-certified connector proof, and not customer traction by itself.

Do not describe Context Engine · Scout as the complete advanced relationship-set engine. Scout owns ingestion, exact data items, assisted imports, customer-owned data-plane mechanics, governance/audit, local APIs, and basic fallback intelligence. Fortress owns production/private scale, advanced relationship sets, attribution paths, comparable-example analysis, outcome matching, canonical scoring, and governed JSON handoff where required. Cloud is commercial/control-plane only.
