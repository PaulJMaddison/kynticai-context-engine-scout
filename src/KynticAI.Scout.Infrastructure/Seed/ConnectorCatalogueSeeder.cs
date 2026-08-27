using System.Text.Json;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Seed;

public static class ConnectorCatalogueSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static async Task SeedAsync(ScoutDbContext dbContext, DateTime utcNow, CancellationToken cancellationToken)
    {
        var existingTypes = await dbContext.ConnectorCatalogueEntries
            .AsNoTracking()
            .Select(x => x.ConnectorType)
            .ToListAsync(cancellationToken);
        var existingTypeSet = existingTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingEntries = BuildEntries(utcNow)
            .Where(entry => !existingTypeSet.Contains(entry.ConnectorType))
            .ToList();

        if (missingEntries.Count == 0)
        {
            return;
        }

        dbContext.ConnectorCatalogueEntries.AddRange(missingEntries);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<ConnectorCatalogueEntry> BuildEntries(DateTime utcNow)
        =>
        [
            Entry("sqlDatabase", "SQL Database", "Generic SQL connector for local demo databases and PostgreSQL-backed deployments.", "Database", ConnectorCatalogueAvailability.OpenCore, ["SqlMetric", "Crm", "ProductUsage"], GenericCapabilities(), SqlConfiguration(), SqlCredentials(), "Opens the configured database connection.", false, 10, utcNow),
            Entry("postgresql", "PostgreSQL", "Public generic alias for the SQL Database connector when an approved PostgreSQL table or view is used as a source.", "Database", ConnectorCatalogueAvailability.OpenCore, ["SqlMetric", "Crm", "ProductUsage"], GenericCapabilities(), SqlConfiguration(), SqlCredentials(), "Resolves to the sqlDatabase connector implementation.", false, 15, utcNow),
            Entry("restApi", "REST API", "Generic REST connector for source systems that expose AI-safe JSON payloads.", "API", ConnectorCatalogueAvailability.OpenCore, ["Crm", "EventStream", "ProductUsage", "SqlMetric"], GenericCapabilities(), RestConfiguration(), RestCredentials(), "HEAD request or static-response validation.", false, 20, utcNow),
            Entry("csvUpload", "CSV / file import", "Demo-safe parsed CSV rows and spreadsheet extracts for local exploration.", "File", ConnectorCatalogueAvailability.OpenCore, ["Crm", "SqlMetric", "ProductUsage", "EventStream"], GenericCapabilities(), CsvConfiguration(), EmptyCredentials(), "Validates parsed row shape only.", false, 30, utcNow),
            Entry("mockCrm", "Mock CRM", "Fictional CRM records for demos, tests, and starter selector previews.", "Demo", ConnectorCatalogueAvailability.OpenCore, ["Crm", "EventStream"], DemoCapabilities(), MockConfiguration("crm"), EmptyCredentials(), "Always local and deterministic.", false, 40, utcNow),
            Entry("mockBilling", "Mock Billing", "Fictional billing records for renewal, plan, invoice, and payment signals.", "Demo", ConnectorCatalogueAvailability.OpenCore, ["SqlMetric", "EventStream"], DemoCapabilities(), MockConfiguration("billing"), EmptyCredentials(), "Always local and deterministic.", false, 50, utcNow),
            Entry("mockSupport", "Mock Support", "Fictional support records for ticket, priority, and sentiment signals.", "Demo", ConnectorCatalogueAvailability.OpenCore, ["Crm", "EventStream"], DemoCapabilities(), MockConfiguration("support"), EmptyCredentials(), "Always local and deterministic.", false, 60, utcNow),
            Entry("productTelemetryEvents", "Product telemetry events", "Public event-contract entry for product usage rollups sent through the source-system event API.", "Event contract", ConnectorCatalogueAvailability.OpenCore, ["ProductUsage", "EventStream"], EventContractCapabilities(), EventContractConfiguration("source.product_usage.rollup_ready"), EmptyCredentials(), "Validated by /api/v1/events/source-system.", false, 70, utcNow),
            Entry("firstPartyConversionEvents", "First-party conversion events", "Public event-contract entry for customer-owned web conversion events such as pricing visits and form submissions.", "Event contract", ConnectorCatalogueAvailability.OpenCore, ["EventStream"], EventContractCapabilities(), EventContractConfiguration("source.web_conversion.received"), EmptyCredentials(), "Validated by /api/v1/events/source-system.", false, 80, utcNow),
            Entry("sqlServer", "SQL Server placeholder", "Paid/private enterprise connector placeholder. The public repo does not include SQL Server-specific handlers, private network deployment, or customer schema mappings.", "Database", ConnectorCatalogueAvailability.Enterprise, ["SqlMetric", "Crm", "ProductUsage"], PlaceholderCapabilities(), PlaceholderConfiguration("sqlServer"), SqlCredentials(), "Unavailable in open source; safe metadata only.", true, 90, utcNow),
            Entry("billing-system", "Billing system connector", "Customer-specific billing connector placeholder. The public repo does not include production invoice, payment, or finance sync clients.", "Billing", ConnectorCatalogueAvailability.Enterprise, ["SqlMetric", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("billing-system"), ApiTokenCredentials(), "Unavailable in open source; customer-specific implementation.", true, 95, utcNow),
            Entry("legacy-dotnet-handlers", "Legacy .NET web handlers", "Customer-specific connector placeholder for old .NET applications that emit approved events or request context packages. The public repo does not include paid .NET handler packages.", ".NET", ConnectorCatalogueAvailability.Enterprise, ["EventStream", "Crm", "SqlMetric"], PlaceholderCapabilities(), PlaceholderConfiguration("legacy-dotnet-handlers"), ApiTokenCredentials(), "Unavailable in open source; customer-specific implementation.", true, 98, utcNow),
            Entry("salesforce", "Salesforce placeholder", "Catalogue placeholder for a future commercial Salesforce connector. No Salesforce implementation ships in this repo.", "CRM", ConnectorCatalogueAvailability.SaaSManaged, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("salesforce"), OAuthCredentials(), "Unavailable in open source; safe metadata only.", true, 100, utcNow),
            Entry("hubspot", "HubSpot placeholder", "Catalogue placeholder for a future commercial HubSpot connector. No HubSpot implementation ships in this repo.", "CRM", ConnectorCatalogueAvailability.SaaSManaged, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("hubspot"), OAuthCredentials(), "Unavailable in open source; safe metadata only.", true, 110, utcNow),
            Entry("dynamics", "Dynamics placeholder", "Catalogue placeholder for a future commercial Microsoft Dynamics connector. No Dynamics implementation ships in this repo.", "CRM", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("dynamics"), OAuthCredentials(), "Unavailable in open source; safe metadata only.", true, 120, utcNow),
            Entry("snowflake", "Snowflake placeholder", "Catalogue placeholder for a future commercial Snowflake connector. No Snowflake implementation ships in this repo.", "Warehouse", ConnectorCatalogueAvailability.ComingSoon, ["SqlMetric", "ProductUsage"], PlaceholderCapabilities(), PlaceholderConfiguration("snowflake"), WarehouseCredentials(), "Unavailable in open source; safe metadata only.", true, 130, utcNow),
            Entry("bigquery", "BigQuery placeholder", "Catalogue placeholder for a future commercial BigQuery connector. No BigQuery implementation ships in this repo.", "Warehouse", ConnectorCatalogueAvailability.ComingSoon, ["SqlMetric", "ProductUsage"], PlaceholderCapabilities(), PlaceholderConfiguration("bigquery"), WarehouseCredentials(), "Unavailable in open source; safe metadata only.", true, 140, utcNow),
            Entry("zendesk", "Zendesk placeholder", "Catalogue placeholder for a future commercial Zendesk connector. No Zendesk implementation ships in this repo.", "Support", ConnectorCatalogueAvailability.SaaSManaged, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("zendesk"), OAuthCredentials(), "Unavailable in open source; safe metadata only.", true, 150, utcNow),
            Entry("netsuite", "NetSuite placeholder", "Catalogue placeholder for a future commercial NetSuite connector. No NetSuite implementation ships in this repo.", "ERP", ConnectorCatalogueAvailability.ComingSoon, ["SqlMetric", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("netsuite"), OAuthCredentials(), "Unavailable in open source; safe metadata only.", true, 160, utcNow),
            Entry("microsoft365-outlook", "Microsoft 365 / Outlook metadata placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Outlook mailbox sync implementation.", "Email", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("microsoft365-outlook"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 170, utcNow),
            Entry("gmail", "Gmail / Google Workspace metadata placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Gmail or Google Workspace mailbox sync implementation.", "Email", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("gmail"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 180, utcNow),
            Entry("slack", "Slack placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Slack workspace or message sync implementation.", "Collaboration", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("slack"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 190, utcNow),
            Entry("microsoft-teams", "Microsoft Teams placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Microsoft Teams sync implementation.", "Collaboration", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("microsoft-teams"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 200, utcNow),
            Entry("outlook-calendar", "Outlook Calendar placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Outlook Calendar event sync implementation.", "Calendar", ConnectorCatalogueAvailability.Enterprise, ["EventStream", "ProductUsage"], PlaceholderCapabilities(), PlaceholderConfiguration("outlook-calendar"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 210, utcNow),
            Entry("google-calendar", "Google Calendar placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Google Calendar event sync implementation.", "Calendar", ConnectorCatalogueAvailability.Enterprise, ["EventStream", "ProductUsage"], PlaceholderCapabilities(), PlaceholderConfiguration("google-calendar"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 220, utcNow),
            Entry("segment", "Segment placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Segment product analytics sync implementation.", "Product analytics", ConnectorCatalogueAvailability.Enterprise, ["ProductUsage", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("segment"), ApiTokenCredentials(), "Unavailable in open source; aggregate metadata only in private packages.", true, 230, utcNow),
            Entry("amplitude", "Amplitude placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Amplitude product analytics sync implementation.", "Product analytics", ConnectorCatalogueAvailability.Enterprise, ["ProductUsage", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("amplitude"), ApiTokenCredentials(), "Unavailable in open source; aggregate metadata only in private packages.", true, 240, utcNow),
            Entry("mixpanel", "Mixpanel placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Mixpanel product analytics sync implementation.", "Product analytics", ConnectorCatalogueAvailability.Enterprise, ["ProductUsage", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("mixpanel"), ApiTokenCredentials(), "Unavailable in open source; aggregate metadata only in private packages.", true, 250, utcNow),
            Entry("posthog", "PostHog placeholder", "Paid/private enterprise connector placeholder. The public repo does not include PostHog product analytics sync implementation.", "Product analytics", ConnectorCatalogueAvailability.Enterprise, ["ProductUsage", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("posthog"), ApiTokenCredentials(), "Unavailable in open source; aggregate metadata only in private packages.", true, 260, utcNow),
            Entry("jira", "Jira placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Jira project or issue sync implementation.", "Work management", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("jira"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 270, utcNow),
            Entry("linear", "Linear placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Linear issue sync implementation.", "Work management", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("linear"), ApiTokenCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 280, utcNow),
            Entry("confluence", "Confluence placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Confluence page sync implementation.", "Knowledge", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("confluence"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 290, utcNow),
            Entry("notion", "Notion placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Notion page or database sync implementation.", "Knowledge", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("notion"), ApiTokenCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 300, utcNow),
            Entry("sharepoint", "SharePoint placeholder", "Paid/private enterprise connector placeholder. The public repo does not include SharePoint document or list sync implementation.", "Knowledge", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("sharepoint"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 310, utcNow),
            Entry("google-drive", "Google Drive placeholder", "Paid/private enterprise connector placeholder. The public repo does not include Google Drive file sync implementation.", "Knowledge", ConnectorCatalogueAvailability.Enterprise, ["Crm", "EventStream"], PlaceholderCapabilities(), PlaceholderConfiguration("google-drive"), OAuthCredentials(), "Unavailable in open source; metadata-only defaults in private packages.", true, 320, utcNow)
        ];

    private static ConnectorCatalogueEntry Entry(
        string connectorType,
        string displayName,
        string description,
        string category,
        ConnectorCatalogueAvailability availability,
        IReadOnlyList<string> supportedDataSourceKinds,
        IReadOnlyList<string> capabilities,
        object configurationSchema,
        object credentialSchema,
        string healthCheckMode,
        bool isPlaceholder,
        int sortOrder,
        DateTime utcNow)
        => ConnectorCatalogueEntry.Create(
            connectorType,
            displayName,
            description,
            category,
            availability,
            JsonSerializer.Serialize(supportedDataSourceKinds, JsonOptions),
            JsonSerializer.Serialize(capabilities, JsonOptions),
            JsonSerializer.Serialize(configurationSchema, JsonOptions),
            JsonSerializer.Serialize(credentialSchema, JsonOptions),
            healthCheckMode,
            true,
            isPlaceholder,
            sortOrder,
            utcNow);

    private static string[] GenericCapabilities() =>
    [
        "configurationValidation",
        "healthCheck",
        "preview",
        "dryRun",
        "scheduledSync",
        "eventTriggeredRecompute",
        "secureCredentialStorage"
    ];

    private static string[] DemoCapabilities() =>
    [
        "configurationValidation",
        "healthCheck",
        "preview",
        "dryRun",
        "eventTriggeredRecompute",
        "secureCredentialStorage"
    ];

    private static string[] PlaceholderCapabilities() =>
    [
        "catalogueOnly",
        "configurationSchema",
        "futureHealthCheck",
        "futureCredentialStorage"
    ];

    private static string[] EventContractCapabilities() =>
    [
        "eventContract",
        "signedWebhook",
        "machineToken",
        "eventTriggeredRecompute",
        "auditTrail"
    ];

    private static object SqlConfiguration() => new
    {
        type = "object",
        required = new[] { "mode", "tableName", "userIdColumn", "columns" },
        properties = new Dictionary<string, object>
        {
            ["mode"] = new { type = "string", @enum = new[] { "currentDatabase", "customerOpsDatabase", "connectionString" } },
            ["tableName"] = new { type = "string" },
            ["userIdColumn"] = new { type = "string" },
            ["tenantSlugColumn"] = new { type = "string" },
            ["columns"] = new { type = "array", items = new { type = "string" } }
        }
    };

    private static object RestConfiguration() => new
    {
        type = "object",
        required = new[] { "baseUrl" },
        properties = new Dictionary<string, object>
        {
            ["baseUrl"] = new { type = "string" },
            ["pathTemplate"] = new { type = "string" },
            ["subjectQueryParameter"] = new { type = "string" },
            ["staticResponses"] = new { type = "array" }
        }
    };

    private static object CsvConfiguration() => new
    {
        type = "object",
        required = new[] { "rows" },
        properties = new Dictionary<string, object>
        {
            ["externalUserIdColumn"] = new { type = "string", @default = "externalUserId" },
            ["observedAtColumn"] = new { type = "string", @default = "observedAtUtc" },
            ["delimiter"] = new { type = "string", @default = "," },
            ["rows"] = new { type = "array" }
        }
    };

    private static object MockConfiguration(string payloadRoot) => new
    {
        type = "object",
        required = new[] { "records" },
        properties = new Dictionary<string, object>
        {
            ["scenario"] = new { type = "string", @default = "safe-local-demo" },
            ["payloadRoot"] = new { type = "string", @const = payloadRoot },
            ["records"] = new { type = "array" }
        }
    };

    private static object EventContractConfiguration(string eventType) => new
    {
        type = "object",
        required = new[] { "eventType", "sourceSystem" },
        properties = new Dictionary<string, object>
        {
            ["eventType"] = new { type = "string", @const = eventType },
            ["sourceSystem"] = new { type = "string" },
            ["workspaceSlug"] = new { type = "string" },
            ["externalUserId"] = new { type = "string" },
            ["externalAccountId"] = new { type = "string" },
            ["payload"] = new { type = "object" }
        }
    };

    private static object PlaceholderConfiguration(string provider) => new
    {
        type = "object",
        description = $"Placeholder schema for future {provider} connector configuration. It is intentionally non-executable in the public repo.",
        properties = new Dictionary<string, object>
        {
            ["provider"] = new { type = "string", @const = provider },
            ["environment"] = new { type = "string" },
            ["syncObjects"] = new { type = "array", items = new { type = "string" } }
        }
    };

    private static object EmptyCredentials() => new
    {
        type = "object",
        properties = new Dictionary<string, object>()
    };

    private static object SqlCredentials() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["connectionString"] = new { type = "string", secret = true }
        }
    };

    private static object RestCredentials() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["bearerToken"] = new { type = "string", secret = true },
            ["apiKey"] = new { type = "string", secret = true },
            ["basicUsername"] = new { type = "string" },
            ["basicPassword"] = new { type = "string", secret = true }
        }
    };

    private static object OAuthCredentials() => new
    {
        type = "object",
        description = "Commercial/private connector credentials would be stored through IConnectorCredentialStore; values are not implemented here.",
        properties = new Dictionary<string, object>
        {
            ["oauthClientId"] = new { type = "string" },
            ["oauthClientSecret"] = new { type = "string", secret = true },
            ["refreshToken"] = new { type = "string", secret = true }
        }
    };

    private static object WarehouseCredentials() => new
    {
        type = "object",
        description = "Commercial/private warehouse credentials would be stored through IConnectorCredentialStore; values are not implemented here.",
        properties = new Dictionary<string, object>
        {
            ["account"] = new { type = "string" },
            ["privateKey"] = new { type = "string", secret = true },
            ["serviceAccountJson"] = new { type = "string", secret = true }
        }
    };

    private static object ApiTokenCredentials() => new
    {
        type = "object",
        description = "Commercial/private connector credentials would be stored through IConnectorCredentialStore; values are not implemented here.",
        properties = new Dictionary<string, object>
        {
            ["apiToken"] = new { type = "string", secret = true },
            ["serviceAccountReference"] = new { type = "string" }
        }
    };
}
