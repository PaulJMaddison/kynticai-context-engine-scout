# Changelog

All notable changes to this project will be documented in this file.

The format is inspired by Keep a Changelog and this project follows semantic versioning.

## [Unreleased]

### Changed

- Migration export tool: default purpose metadata renamed from scout-fortress-migration-export to scout-open-core-migration-export (exported artefacts now carry the new value).

### Security

- Hardened cross-tenant authorisation on the admin REST endpoints (`/audit-events/export`, `/admin/organisation`, `/admin/users`, `/admin/users/{id}`, `/blueprints`, `/governance/policies`): a tenant-scoped request that names another tenant's slug is now rejected with `403 authorization.denied` instead of being resolved without an ownership check. Platform owners and system actors keep explicit cross-tenant access.

## [2.8.0] - 2026-05-21

### Added

- Merged the v2-next public core, establishing the foundational v2.x open-source codebase with domain entities, selectors, context snapshots, GraphQL/REST API, React frontend, and TypeScript/C# SDKs.
- Overhauled the README, CONTRIBUTING guide, SDK JSDoc documentation, GitHub issue and pull request templates, and `.editorconfig` for open-source community readiness.
- Added GitHub Actions CI and release workflows for automated build, test, and packaging (currently disabled to conserve Actions minutes).
- Added a getting started guide with Docker Compose deployment configuration and `.env.example` for streamlined onboarding.
- Added release process documentation, cross-repository changelog coordination, and release automation scripts.
- Added Scalar API documentation UI at `/api-docs` with OpenAPI specification export tooling, preserving the existing Swagger UI at `/swagger`.
- Added a comprehensive end-to-end integration test suite bringing the total to 127 tests (54 E2E, 32 unit, 30 integration, 11 SDK).

### Fixed

- Fixed a cross-platform SDK URI resolution bug and added Bash equivalents for previously PowerShell-only scripts.
- Updated pilot-readiness scripts to ignore `.disabled` workflow files when scanning for CI configuration.
- Corrected `find` command precedence and PowerShell `-Include` parameter usage in pilot-readiness scripts.
- Fixed Content Security Policy blocking JavaScript and CSS on Scalar and Swagger documentation paths.

### Changed

- Added a testing skill for the Scout open-source repository to improve session reproducibility.
- Updated the testing-scout-backend skill with Scalar documentation paths and CSP troubleshooting guidance.

### Chores

- Disabled GitHub Actions workflows (renamed `.yml` to `.yml.disabled`) to conserve Actions minutes during pre-launch development.

## [2.7.0] - 2026-05-13

### Added

- Added customer data-plane installation and production-readiness runbooks for paid-pilot delivery.
- Added commercial readiness, hosting alignment, observability, machine-to-machine identity, support expectation, privacy, cookie/consent, terms, and paid-pilot legal draft documentation.
- Added production environment readiness checks for production-style data-plane configuration.

### Changed

- Updated public release metadata to `2.7.0`.
- Strengthened the public open-core story around demo caveats, install runbooks, public API/SDK/context package docs, and paid-pilot boundaries.

### Security

- Reconfirmed that the public repo does not include paid enterprise implementations, private cloud/control-plane implementation, real licence signing keys, customer-specific code, or raw customer operational data.
- Reconfirmed that this release prepares for hosted and paid-pilot readiness but is not a complete self-serve SaaS release.

## [2.6.0] - 2026-05-13

### Added

- Added buyer-facing docs for the customer data plane, public API contract, integration layer, and buyer FAQ.
- Expanded the retired preview demo copy with a richer fictional data-plane story, connector catalogue framing, and proof/caveat copy.
- Added REST, GraphQL, TypeScript SDK, and .NET SDK coverage for context facts, context snapshots, governed evidence packages, selector preview/validation, source-system events, pagination, errors, and machine-to-machine auth.

### Changed

- Strengthened README and product docs around the nervous-system narrative, existing systems staying in place, bring-your-own AI, paid-pilot path, and open-core versus paid/private boundaries.
- Updated public release metadata to `2.6.0`.

### Security

- Reconfirmed that paid enterprise connector implementations, cloud billing/control-plane implementation, customer-specific code, secrets, and raw customer data are not part of the public repository.
- Reconfirmed that public demo materials use fictional data and are not live backend or SaaS functionality.

## [2.5.1] - 2026-05-12

### Added

- Retired the temporary hosted preview-demo publishing path ahead of the company website launch.
- Removed temporary hosted-preview references from the README and demo documentation.
- Removed preview-demo publishing artefacts from the web build output.

### Changed

- Updated public release metadata to `2.5.1`.

## [2.5.0] - 2026-05-12

### Added

- Added a temporary brochure/demo preview for sales, marketing, and buyer education.
- Added fictional fixtures for Northstar Logistics, User 123 / Avery Stone, raw operational signals, semantic facts, selector definitions, grounded AI output, audit provenance, and interaction timelines.
- Added a temporary preview build path that has since been retired in favour of the main KynticAI website.
- Added demo documentation and captured laptop/mobile screenshots.

### Changed

- Clarified that the temporary preview site is not the product runtime: the downloadable React app, backend, APIs, SDKs, selector engine, seeded demo, and local setup path remain the functional product experience.
- Updated public release metadata to `2.5.0`.
- Expanded paid-pilot and hosted-deployment documentation for the public commercial positioning.

### Security

- Kept the temporary preview demo backend-free with fictional snapshot data only. It does not include customer operational data, secrets, live connector execution, database access, or paid/private connector implementation code.

## [2.4.1] - 2026-05-12

### Added

- Added a dedicated paid pilot landing page at `/pilot` with a single pilot-scope CTA, lead capture fields, verified SQL connector proof messaging, customer-owned data-plane positioning, pilot inclusions/exclusions, 2-6 week timeline, and anonymised ERP platform pattern.
- Added public privacy and terms/disclaimer pages with operator/contact configuration and legal-review caveats.
- Added configurable `VITE_PILOT_LEAD_ENDPOINT` support so the pilot form can submit to the private cloud mini CRM, with email fallback when no endpoint is configured.

### Changed

- Rewrote public homepage and marketing page headings to remove "This page..." style copy.
- Updated web Docker and environment documentation for pilot lead endpoint and site/operator settings.

### Security

- Kept the public repository boundary clean: the landing page can post enquiry metadata to the private cloud CRM, but the public repo does not include raw customer operational data handling or paid connector implementation code.

## [2.4.0] - 2026-05-12

### Added

- Added first paid pilot one-pager and pilot SOW template for implementation-led paid pilot conversations.
- Added production install rehearsal documentation plus PowerShell and Bash rehearsal scripts covering production mode, PostgreSQL, strong signing keys, persistent Data Protection keys, migrations, backup/restore, health, REST, GraphQL, OpenTelemetry, and machine authentication checks.
- Added a first real connector proof for the generic SQL connector, including sample connector configuration, sample source SQL, API lookup documentation, and audit/provenance expectations.
- Added legal and support starter templates for pilot agreement outline, data processing assumptions, security/privacy appendix, support model, onboarding checklist, and incident response outline.

### Changed

- Strengthened the SQL connector integration proof from preview-only mapping to source row, selector execution, context fact, context snapshot, service/API-shaped lookup, provenance metadata, and recompute audit verification.
- Linked the paid pilot document to the new one-pager, SOW, production rehearsal, connector proof, and legal outline.
- Updated public package and release metadata to `2.4.0`.

### Security

- Kept the public repository boundary clean: no vendor-specific paid connector implementation, no private cloud implementation, no customer secrets, and no raw customer data.
- Reinforced that customer-owned data planes keep operational data, credentials, context facts, snapshots, selectors, and local audit logs out of hosted control-plane services by default.

## [2.3.0] - 2026-05-12

### Added

- Added first-class webhook signing secrets with create, list, rotate, and revoke REST endpoints, hashed storage, workspace scoping, timestamp checks, replay checks, and audit events.
- Added a public-safe governance hook seam so private enterprise policy packages can mask or suppress returned context without placing paid implementation code in the public repository.
- Added a canonical API scope contract document and pilot readiness scripts for local paid-pilot checks without GitHub Actions.
- Added a paid pilot readiness runbook covering PostgreSQL smoke checks, backup/restore rehearsal, support bundle dry runs, upgrade/rollback rehearsal, and customer handover.

### Changed

- Formalised `context:write` as an official scope and aligned public docs, SDK examples, and REST behaviour around the canonical scope set.
- Reworded public copy towards paid pilot, customer data plane, self-hosted context infrastructure, and future/private cloud control-plane language.
- Kept legacy API-key webhook HMAC compatibility, but marked dedicated webhook signing secrets as the recommended production model.

### Security

- Kept the public repository limited to open-core code, public interfaces, no-op/default implementations, placeholders, docs, scripts, and examples with fictional data.
- Reconfirmed that paid enterprise connector implementations, private governance policy implementation, and hosted cloud/control-plane implementation remain outside the public repository.
- Extended local artefact ignore coverage for runtime data, logs, generated licences, keys, certificates, and support bundles.

## [2.2.0] - 2026-05-12

### Added

- Added public-safe catalogue placeholders and docs for paid/private email, chat, calendar, product analytics, work management, and knowledge-system connector families.
- Added release notes for the v2.2.0 public open-core release aligned with the private enterprise connector-suite release.
- Added paid pilot documentation, a production install checklist, and an anonymised ERP platform implementation pattern for buyer proof.

### Changed

- Clarified that real Microsoft 365 / Outlook, Gmail / Google Workspace, Slack, Microsoft Teams, Outlook Calendar, Google Calendar, Segment, Amplitude, Mixpanel, PostHog, Jira, Linear, Confluence, Notion, SharePoint, and Google Drive implementations live in private enterprise packages.
- Folded in public data-plane hardening for REST/GraphQL scope checks, correlation-friendly errors, and webhook safety documentation.
- Updated pricing, commercial, docs, connector catalogue, open-core, and integrations copy for a paid-pilot-first commercial motion rather than complete self-serve SaaS.
- Updated public version metadata to `2.2.0`.

### Security

- Reconfirmed that the public repo contains placeholders/interfaces only for paid enterprise connectors and does not include private connector implementation code.
- Documented the metadata-only and explicit opt-in safety model for communication and knowledge connectors.
- Added production guidance that `VITE_DEMO_FALLBACK=true` is local-demo only and must not mask API failures in customer environments.

## [2.1.1] - 2026-05-11

### Security

- Pinned the released TanStack frontend dependencies to exact known-installed versions after a reported npm supply-chain advisory affecting later `@tanstack/*` package versions.
- Kept the locked web app on `@tanstack/react-query` `5.100.9`, `@tanstack/react-query-devtools` `5.100.9`, `@tanstack/react-router` `1.169.2`, and `@tanstack/react-router-devtools` `1.166.13`.
- Documented that release builds should use `npm ci` with the committed lockfile rather than floating dependency ranges during active supply-chain incidents.

## [2.1.0] - 2026-05-11

### Added

- Prepared the public open-core relaunch with clearer commercial boundaries across README, docs, and marketing copy.
- Added release notes for the coordinated public, enterprise, and cloud repository split.
- Clarified context consumer patterns for AI tools, workflows, reports, applications, copilots, agents, and non-AI automation.
- Documented product positioning for CEOs, CTOs, product teams, and engineers.

### Changed

- Updated public version metadata to `2.1.0`.
- Reframed public SaaS wording as SaaS/control-plane foundations rather than hosted SaaS implementation.
- Clarified that paid enterprise connectors, SSO/SAML, SCIM, vault integrations, advanced governance, compliance exports, deployment packs, SLA tooling, hosted account management, billing, licence portals, download portals, update channels, support portals, and cloud operations are paid/private offerings outside the public repo.
- Tightened website and README copy so enterprise/cloud options are marketed without implying their code ships in open source.

### Security

- Re-audited the public boundary so real enterprise/cloud implementation code, private signing keys, vendor credentials, customer-specific code, and raw customer data remain out of the public repository.
- Kept blueprint import as user-provided JSON; it does not call external AI APIs.

## [2.0.0] - 2026-05-11

### Added

- Repositioned KynticAI Scout as open-core context infrastructure rather than primarily an AI app.
- Added marketing pages for the platform, use cases, integrations, open-core model, pricing/deployment, docs, demo, and FAQ.
- Added a self-hosted customer admin console covering organisation settings, workspace settings, users and roles, API clients, usage and limits, connector catalogue, webhook events, blueprint imports, data governance, audit export, and licence/update status.
- Added SaaS-control-plane foundations for tenants, workspaces, users, subscriptions, API clients, connectors, selector definitions, context packages, audit events, billing usage, onboarding, feature flags, and hosted-mode configuration.
- Added customer-owned data-plane architecture documentation and configuration for optional control-plane connection settings, local licence files, offline grace periods, and community mode.
- Added production-minded JWT authentication, API client authentication, API key hashing, rotation, revocation, and last-used tracking.
- Added versioned REST API v1 endpoints with tenant/workspace scoping, API key auth, pagination, filtering, correlation IDs, consistent error responses, and OpenAPI metadata.
- Added webhook/source-system event ingestion with signatures, idempotency, event history, selector trigger matching, recompute job creation, dead-letter handling, and audit events.
- Added connector marketplace skeleton with open-core connectors, demo-safe mock connectors, enterprise/vendor placeholders, health-check and credential abstractions, and clear public repo boundaries.
- Added AI-assisted blueprint JSON import with schema, validation, preview, apply, REST/GraphQL endpoints, audit events, and Bootstrap Studio UI.
- Added billing and usage metering foundations with Free, Pro, Business, and Enterprise plan metadata without a live payment-provider integration.
- Added TypeScript and .NET SDK scaffolds for context consumers.
- Added PostgreSQL hosted-mode, Docker, Render deployment guidance, backup/restore documentation, support bundle guidance, and local SQLite setup preservation.
- Added maintainer guardrails for the open core boundary across the README, contributing guide, security guidance, roadmap, and boundary-specific documentation.
- Added public enterprise extension contracts and safe default implementations so future private enterprise packages can plug into the open source core without forking it.
- Added explicit documentation for the expected public, enterprise, and optional future cloud repository split.

### Changed

- Updated README, docs, and website copy to explain that Scout creates governed context for customer-owned systems, reports, workflows, copilots, agents, and apps.
- Clarified that the AI sales playground is one example consumer, not the required product architecture.
- Documented the split between a future hosted control plane and the self-hosted customer data plane, including what data must stay local.
- Clarified the commercial model so the public repository reads as a credible open source core rather than a stripped-down teaser.
- Reworked connector wording and examples to stay generic in the public repo rather than implying that premium commercial connectors ship here today.
- Replaced real-looking demo organisations and domains with clearly fictional names and reserved example domains.
- Updated public package and app descriptions so they describe the project as an open source core, demo, and admin console rather than a commercial-only demo.

### Removed

- Removed an internal strategy document that did not belong in the public repository.
- Removed stale v1.2 release notes because the productisation scope is now released as v2.0.0.

### Security

- Added tenant and workspace scoping checks across admin, GraphQL, REST, API-client, and event-ingestion flows.
- Added permission-denied audit coverage for blocked role changes and cross-tenant access attempts.
- Preserved the rule that raw customer operational data, connector credentials, context facts, and prompt packages do not need to leave the customer data plane.
- Tightened public-repo hygiene by standardising development-only signing key placeholders, extending ignore rules for likely private artefacts, and reinforcing guidance against committing secrets, customer data, or paid enterprise implementation code.

### Fixed

- Capped percentage selector outputs so formula-based semantic attributes cannot display impossible values above 100%.
- Updated the expansion potential demo selector and Bootstrap Studio blueprint to use a realistic capped score.
- Hardened the Windows reset script so it stops repo API processes before deleting SQLite demo databases.
- Hardened REST admin tenant resolution so cross-tenant admin reads are rejected instead of silently falling back to the actor tenant.

## [1.1.0] - 2026-05-10

### Added

- Outcome-led page positioning across the product so every screen explains what it does for the user before showing configuration, data, or controls.
- More detailed fresh-laptop setup documentation for the default SQLite demo path, repo-local .NET and Node bootstrap, restart, reset, and verification commands.

### Changed

- Rewrote the landing hero to clearly state that Scout turns existing business data into AI-ready context.
- Updated dashboard, data source, selector builder, schema registry, customer context, AI playground, audit, bootstrap, and walkthrough page headers to lead with user value.
- Refreshed README screenshots from the latest running SQLite-backed UI.

### Removed

- Removed GitHub Actions workflows so the repository does not consume paid GitHub Actions minutes.

## [1.0.0] - 2026-05-10

### Added

- Production-ready local demo flow with dual-database operational and semantic context separation.
- Default SQLite laptop install path with repo-local .NET 10 and Node.js bootstrap so the demo can run without Docker, PostgreSQL, or preinstalled developer tooling.
- Optional Docker/PostgreSQL mode for production-like package demos and observability.
- Executive walkthrough pages covering legacy source signals, semantic timelines, AI interaction timelines, rollout, ROI, and governance.
- Bootstrap Studio for Codex or Claude assisted source-system analysis, prompt generation, blueprint upload, and import into Scout.
- Responsive regression coverage for login, mobile, and core product routes.

### Changed

- Promoted the project to `1.0.0` as the first complete commercial demo release.
- Reframed local setup around a zero-friction SQLite quick start while preserving the two-database architecture and PostgreSQL path.
- Refined the login experience so laptop and desktop viewports fit without scrolling, while mobile remains naturally scrollable.
- Improved the customer profile People panel, provenance panels, JSON viewers, and app shell responsiveness.
- Refreshed README screenshots from the live running app.

### Fixed

- Fixed prompt-template audit serialization cycles during Bootstrap Studio imports.
- Fixed clipped profile rows, overflowing code panels, taskbar-unsafe demo browser sizing, and repeated responsive layout regressions.

## [0.1.1] - 2026-05-09

### Added

- Executive demo storytelling flow at `/demo` covering business value, technical integration posture, and rollout credibility.
- Cross-system Scout event timeline showing how raw operational events become semantic business meaning.
- AI-assisted onboarding narrative showing how tools like Codex or Claude can draft a discovery report, semantic blueprint, and selector candidates for admin review.
- Refreshed screenshot gallery including executive demo, Scout timeline, and AI bootstrap visuals.

### Changed

- Reframed the demo from CEO-only language to broader executive and technical decision-maker language.
- Improved the customer context viewer so the transformation from source events to semantic interpretation is directly visible.
- Updated release metadata and package versions to `0.1.1`.

### Release notes

- This follow-up release is focused on making the product easier to sell and evaluate: clearer narrative, stronger technical credibility, and better documentation for live demos.

## [0.1.0] - 2026-05-09

### Added

- Initial open-source release of the KynticAI Scout commercial demo.
- Dual-database architecture demonstrating separation between operational source data and semantic AI context.
- React frontend for dashboard, data sources, selector builder, schema registry, context viewer, AI playground, and audit log.
- ASP.NET Core + GraphQL backend with selector execution, grounded context generation, and seed data.
- Demo bootstrap scripts, Docker Compose configuration, and seeded executive walkthrough data.
- Screenshot-driven README and architecture documentation.

### Release notes

- Licensed under MIT for permissive reuse, modification, and commercial experimentation.
- Prepared as the first public release for local demos, evaluation, and extension by other teams.
