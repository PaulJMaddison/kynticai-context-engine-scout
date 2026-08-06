# Connector Catalogue

The connector catalogue explains which Scout integration paths are public open-core examples, which are paid/private enterprise implementations, and which entries are roadmap placeholders.

Scout is the nervous system, not the brain. Connectors bring approved operational signals into the customer data plane so selectors can create semantic context for customer-owned AI tools, workflows, apps, reports, and agents. This public repository must therefore show the integration shape without shipping paid enterprise connector logic.

This page is the public connector status record. Keep it factual, replayable, and free of customer-specific delivery plans or private commercial strategy.

## Labels

| Label | Meaning | Public repo status |
| --- | --- | --- |
| Public generic example | Safe executable example or contract useful for local demos and first proofs. | May include runnable code when it is generic, fictional, and not vendor-specific paid implementation. |
| Paid enterprise implementation | Commercial connector or deployment module delivered privately for a customer or supported paid package. | Metadata and docs only. No authentication, sync, credential handling, vendor API logic, or production handler is included here. |
| Planned connector | A roadmap item that helps buyers understand direction. | Catalogue entry or doc note only. It is not ready to promise as working product. |
| Placeholder | A visible non-executable entry used to describe capability boundaries. | Must not register a runtime plugin or pretend to ingest data. |
| Customer-specific connector | Custom integration for one customer estate, mapping, security posture, or network boundary. | Private delivery only. Never committed to the public repo. |

## Readiness Labels

The web console also shows first-slice connector readiness labels:

| Label | Meaning |
| --- | --- |
| Executable open-core | A registered public Scout plugin is available in the current build. |
| Mock/local proof | Suitable for deterministic demo, dry-run, approved export, or provider-neutral event proof. |
| Private/customer-specific | Requires scoped private implementation or customer-specific delivery work. |
| Placeholder | Catalogue metadata only; no executable vendor connector is included in the public repo. |
| Not vendor-certified | The listing must not be read as vendor certification or vendor approval. |

Placeholder entries are metadata only: they register no runtime plugin, do not
ingest data, and imply no vendor certification or vendor approval. A "Placeholder"
badge never means a connector is expected to work from the open source.

These labels are deliberately conservative. A connector can be useful for a paid-pilot discussion while still requiring customer-approved validation before operational use.

## Public Catalogue

| Connector family | Public label | What the public repo may contain | What is not included publicly |
| --- | --- | --- | --- |
| SQL Server | Paid enterprise implementation / placeholder | Generic SQL table connector contracts, selector examples, and sample SQL-shaped data through the open-core SQL connector. | Customer SQL Server handlers, private network deployment packs, credential vault wiring, or customer schema mappings. |
| PostgreSQL | Public generic example | A `postgresql` catalogue entry and alias that resolves to the open-core SQL connector for approved tables or views. | Managed production connector operations, customer credentials, or private schema packages. |
| REST API | Public generic example | Generic REST connector contracts, configuration schemas, and mock responses. | Vendor-specific OAuth flows or production sync clients. |
| CSV/file import | Public generic example | Safe file import examples using fictional data. | Customer extracts, scheduled managed ingestion, or file-store credentials. |
| CRM | Placeholder / paid enterprise implementation | Category metadata, selector examples, and mock CRM demo data. | Real CRM sync, vendor API clients, or production contact/account ingestion. |
| HubSpot | Placeholder / paid enterprise implementation | Catalogue metadata and docs only. | HubSpot OAuth, sync jobs, field mapping packs, or private app credentials. |
| Salesforce | Placeholder / paid enterprise implementation | Catalogue metadata and docs only. | Salesforce authentication, SOQL sync, package install logic, or customer object mappings. |
| Dynamics | Placeholder / paid enterprise implementation | Catalogue metadata and docs only. | Dynamics/Dataverse authentication, sync, or customer-specific entity mappings. |
| SharePoint | Placeholder / paid enterprise implementation | Metadata-only catalogue entry and boundary docs. | Document body ingestion, attachment ingestion, tenant permissions, or production Microsoft Graph sync. |
| Outlook/Microsoft 365 metadata | Placeholder / paid enterprise implementation | Metadata-only entry describing safe email/calendar metadata patterns. | Message bodies, attachments, mailbox sync, Graph credentials, or production tenant consent handling. |
| Gmail/Google Workspace metadata | Placeholder / paid enterprise implementation | Metadata-only entry describing safe email/calendar metadata patterns. | Message bodies, attachments, OAuth clients, mailbox sync, or production tenant consent handling. |
| Zendesk | Placeholder / paid enterprise implementation | Catalogue metadata and support-signal examples. | Zendesk API sync, ticket bodies, attachment ingestion, or tenant credentials. |
| NetSuite | Placeholder / paid enterprise implementation | Catalogue metadata and ERP/billing pattern docs. | NetSuite authentication, SuiteScript, transaction sync, or customer account mappings. |
| Billing | Placeholder / customer-specific connector | Mock billing signals and semantic examples. | Stripe, Paddle, NetSuite billing, ERP finance integrations, or invoice/payment sync clients. |
| Product telemetry | Public generic example / customer-specific connector | Executable source-system event contract entry for product usage rollups plus mock activity scores. | Customer analytics warehouses, proprietary product event schemas, or managed telemetry pipelines. |
| First-party conversion events | Public generic example | Executable source-system event contract entry and fictional web-conversion signals. | Customer web trackers, tag-manager deployments, or production marketing attribution logic. |
| Legacy .NET web handlers | Paid enterprise implementation / customer-specific connector | Interface docs describing how old .NET applications can call Scout or emit approved events. | Paid .NET handler packages, IIS deployment modules, customer code adapters, or private network installers. |

## Public Repo Boundary

Included executable connector paths should remain generic and safe:

- SQL Database / SQL table examples
- REST API examples
- CSV upload examples
- mock CRM, billing, support, usage, and web-conversion data for local demos
- extension interfaces such as `IConnectorPlugin` and connector catalogue metadata

Catalogue-only or paid/private entries intentionally do not:

- register executable connector plugins
- authenticate to vendor APIs
- sync vendor records
- ingest message bodies, document bodies, attachments, or analytics payloads
- store vendor-specific credentials in this public repository
- include customer-specific mappings, selectors, deployment scripts, or support bundles

Real paid implementations live in private enterprise packages or customer delivery repositories.

## Backend Model

Catalogue rows live in `saas_connector_catalogue_entries`. Each row stores:

- connector type and display metadata
- category
- availability: `OpenCore`, `Enterprise`, `SaaSManaged`, or `ComingSoon`
- public status: `PublicGenericExample`, `PaidEnterpriseImplementation`, `PlannedConnector`, or `CustomerSpecificConnector`
- supported data-source kinds
- capabilities
- configuration schema JSON
- credential schema JSON
- health-check mode
- placeholder flag

The seed runs during application bootstrap so local demo, backend-only mode, and hosted mode all expose the same catalogue metadata.

## Extension Points

Runtime connector implementations use `IConnectorPlugin`. Safe open-core plugins can validate configuration, run health checks, fetch records, and return normalised payloads. See the [Connector Authoring Tutorial](connector-authoring-tutorial.md) and [connector-authoring.md](connector-authoring.md) for the authoring flow.

Credential handling stays behind `IConnectorCredentialStore`. Configuration schemas can describe secrets, but runtime implementations should persist secret values as protected references and should not store plaintext credentials in connector configuration.

Health checks stay behind `IConnectorPlugin.CheckHealthAsync`. Placeholder marketplace entries describe future health-check behaviour but are not registered as executable plugins.

## API Surface

REST:

```http
GET /api/v1/connectors/catalogue?page=1&pageSize=100
GET /api/v1/connectors/catalogue?availability=OpenCore
GET /api/v1/connectors/catalogue?q=salesforce
```

GraphQL:

```graphql
query ConnectorCatalogue {
  connectorCatalogue {
    connectorType
    displayName
    publicStatus
    availability
    isIncludedInOpenCore
    requiresCommercialAgreement
    isPlaceholder
    configurationSchemaJson
    credentialSchemaJson
  }
}
```

Frontend:

- `/connectors` shows the catalogue, filters by availability, and clearly labels enterprise/vendor entries as placeholders.

## Verified Open-Core Code Paths

The public seed data and tests now assert the connector boundary in executable code:

- `postgresql` is present as a public generic example and resolves to the `sqlDatabase` connector alias.
- `productTelemetryEvents` and `firstPartyConversionEvents` are public event-contract entries that use `POST /api/v1/events/source-system`.
- `sqlServer`, `billing-system`, `legacy-dotnet-handlers`, and the CRM, ERP, warehouse, support, email, calendar, collaboration, product-analytics, work-management, and knowledge-system vendor entries remain placeholders or paid/private metadata.
- The REST and GraphQL catalogue responses expose `publicStatus` so external docs, frontends, and SDK consumers can distinguish open-core examples from commercial or planned entries.
