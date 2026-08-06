# KynticAI Scout -- Connector Template

This folder contains a minimal connector template that implements the public
`IConnectorPlugin` interface from `KynticAI.Scout.Application.Abstractions`.

Use it as a starting point when authoring a new connector for Scout.

## Files

| File | Purpose |
|---|---|
| `TemplateConnectorPlugin.cs` | Skeleton connector implementing `ConnectorPluginBase`. |
| `template-connector-config.json` | Sample configuration JSON for the template connector. |

## Quick Start

1. Copy `TemplateConnectorPlugin.cs` into your connector project.
2. Rename the class and update `ConnectorType`, `DisplayName`, and `Description`.
3. Implement `GetConfigurationSchema()`, `GetSampleConfiguration()`, and `FetchAsync()`.
4. Register the plugin in your DI container as `IConnectorPlugin`.
5. Run the `ConnectorMetadataValidator` helper to verify your metadata is well-formed.
6. Add a public manifest with `eventShape` if the connector emits source-system events.

## Constraints

- Connectors must not call external AI models.
- Connectors must return deterministic provenance in `ConnectorFetchResult`.
- Configuration schemas must be valid JSON Schema objects.
- Credential fields must use `secret://` references when persisted.
- Event-shaped records should pass `ConnectorContractRules.ValidateIngestEvent(...)`.
- See `docs/connector-authoring-tutorial.md` for the end-to-end walkthrough and
  `docs/connector-authoring.md` for the full technical specification.
