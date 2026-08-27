using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Persistence;
using KynticAI.Scout.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KynticAI.Scout.UnitTests;

public sealed class ConnectorPluginModelTests
{
    [Fact]
    public void ConnectorRegistry_ResolvesAliases_ToRegisteredPlugin()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<IConnectorRegistry>();

        Assert.True(registry.TryGetPlugin("crmApi", out var crmApi));
        Assert.NotNull(crmApi);
        Assert.Equal("restApi", crmApi.ConnectorType);

        Assert.True(registry.TryGetPlugin("fileUpload", out var fileUpload));
        Assert.NotNull(fileUpload);
        Assert.Equal("mock", fileUpload.ConnectorType);

        Assert.True(registry.TryGetPlugin("csvUpload", out var csvUpload));
        Assert.NotNull(csvUpload);
        Assert.Equal("csvUpload", csvUpload.ConnectorType);

        Assert.True(registry.TryGetPlugin("postgresql", out var postgresql));
        Assert.NotNull(postgresql);
        Assert.Equal("sqlDatabase", postgresql.ConnectorType);
    }

    [Fact]
    public void ConnectorRegistry_OnlyRegistersSafeOpenCoreConnectors()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<IConnectorRegistry>();
        var registeredTypes = registry.GetPlugins().Select(static plugin => plugin.ConnectorType).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("mockCrm", registeredTypes);
        Assert.Contains("mockBilling", registeredTypes);
        Assert.Contains("mockSupport", registeredTypes);
        Assert.Contains("csvUpload", registeredTypes);
        Assert.DoesNotContain("sqlServer", registeredTypes);
        Assert.DoesNotContain("billing-system", registeredTypes);
        Assert.DoesNotContain("legacy-dotnet-handlers", registeredTypes);
        Assert.DoesNotContain("salesforce", registeredTypes);
        Assert.DoesNotContain("hubspot", registeredTypes);
        Assert.DoesNotContain("dynamics", registeredTypes);
        Assert.DoesNotContain("snowflake", registeredTypes);
        Assert.DoesNotContain("bigquery", registeredTypes);
        Assert.DoesNotContain("zendesk", registeredTypes);
        Assert.DoesNotContain("netsuite", registeredTypes);
        Assert.DoesNotContain("microsoft365-outlook", registeredTypes);
        Assert.DoesNotContain("gmail", registeredTypes);
        Assert.DoesNotContain("slack", registeredTypes);
        Assert.DoesNotContain("microsoft-teams", registeredTypes);
        Assert.DoesNotContain("outlook-calendar", registeredTypes);
        Assert.DoesNotContain("google-calendar", registeredTypes);
        Assert.DoesNotContain("segment", registeredTypes);
        Assert.DoesNotContain("amplitude", registeredTypes);
        Assert.DoesNotContain("mixpanel", registeredTypes);
        Assert.DoesNotContain("posthog", registeredTypes);
        Assert.DoesNotContain("jira", registeredTypes);
        Assert.DoesNotContain("linear", registeredTypes);
        Assert.DoesNotContain("confluence", registeredTypes);
        Assert.DoesNotContain("notion", registeredTypes);
        Assert.DoesNotContain("sharepoint", registeredTypes);
        Assert.DoesNotContain("google-drive", registeredTypes);
    }

    [Fact]
    public async Task ConnectorCatalogue_SeedsPublicFamiliesAndPaidPlaceholders_WithoutExecutablePlugins()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        await ConnectorCatalogueSeeder.SeedAsync(dbContext, new DateTime(2026, 05, 11, 12, 0, 0, DateTimeKind.Utc), CancellationToken.None);
        var entries = await dbContext.ConnectorCatalogueEntries.ToListAsync();
        var registry = scope.ServiceProvider.GetRequiredService<IConnectorRegistry>();

        foreach (var connectorType in RequiredPublicCatalogueTypes)
        {
            Assert.Contains(entries, entry => entry.ConnectorType == connectorType);
        }

        var openCoreTypes = entries
            .Where(x => x.Availability == ConnectorCatalogueAvailability.OpenCore)
            .Select(x => x.ConnectorType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("postgresql", openCoreTypes);
        Assert.Contains("productTelemetryEvents", openCoreTypes);
        Assert.Contains("firstPartyConversionEvents", openCoreTypes);

        var privatePlaceholders = entries
            .Where(x => PrivatePlaceholderTypes.Contains(x.ConnectorType))
            .ToList();
        Assert.Equal(PrivatePlaceholderTypes.Length, privatePlaceholders.Count);
        Assert.All(privatePlaceholders, entry =>
        {
            Assert.True(entry.IsPlaceholder);
            var description = entry.Description.ToLowerInvariant();
            Assert.True(
                description.Contains("public repo does not include", StringComparison.Ordinal)
                || description.Contains("ships in this repo", StringComparison.Ordinal)
                || description.Contains("not included publicly", StringComparison.Ordinal),
                $"Connector '{entry.ConnectorType}' should state that implementation code is not public.");
            Assert.False(registry.TryGetPlugin(entry.ConnectorType, out _));
        });
    }

    [Fact]
    public async Task ProtectedCredentialStore_PersistsSecretRefs_AndResolvesValues()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var store = scope.ServiceProvider.GetRequiredService<IConnectorCredentialStore>();
        var tenantId = Guid.NewGuid();
        var dataSource = DataSource.Create(tenantId, "REST", "rest", DataSourceKind.Crm, """{"connectorType":"restApi"}""", DateTime.UtcNow);
        dbContext.DataSources.Add(dataSource);
        await dbContext.SaveChangesAsync();

        var refs = await store.PersistCredentialsAsync(
            tenantId,
            dataSource.Id,
            "restApi",
            new JsonObject
            {
                ["apiKey"] = "super-secret"
            },
            CancellationToken.None);

        var resolved = await store.ResolveConfigurationSecretsAsync(
            tenantId,
            new JsonObject
            {
                ["connectorType"] = "restApi",
                ["credentials"] = refs
            },
            CancellationToken.None);

        Assert.Equal("secret://", refs["apiKey"]!.GetValue<string>()[..9]);
        Assert.Equal("super-secret", resolved["credentials"]!["apiKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task RestConnector_UsesStaticResponses_ForPreviewCompatibleFetches()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IConnectorRegistry>();
        var plugin = registry.GetRequiredPlugin("crmApi");

        var user = UserProfile.Create(Guid.NewGuid(), "123", "Avery Stone", "avery@example.com", "Northstar", "VP RevOps", "enterprise", DateTime.UtcNow, DateTime.UtcNow);
        var dataSource = DataSource.Create(Guid.NewGuid(), "CRM API", "test", DataSourceKind.Crm, "{}", DateTime.UtcNow);
        var selector = SelectorDefinition.Create(
            dataSource.TenantId,
            dataSource.Id,
            Guid.NewGuid(),
            "Stage Selector",
            "test",
            SelectorMappingKind.DirectFieldMapping,
            """{"rule":{"valuePath":"crm.stage"}}""",
            "Stage {{sourceValue}}",
            """{"requiredPaths":["crm.stage"]}""",
            0.9m,
            60,
            1,
            30,
            DateTime.UtcNow);

        var result = await plugin.FetchAsync(
            new ConnectorFetchRequest(
                "crmApi",
                selector,
                dataSource,
                user,
                new JsonObject
                {
                    ["baseUrl"] = "https://api.example.com",
                    ["staticResponses"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["externalUserId"] = "123",
                            ["observedAtUtc"] = "2026-05-11T12:00:00Z",
                            ["payload"] = new JsonObject
                            {
                                ["crm"] = new JsonObject
                                {
                                    ["stage"] = "proposal"
                                }
                            }
                        }
                    }
                },
                new JsonObject(),
                ConnectorRunMode.Preview,
                new ConnectorExecutionTrigger("Preview", null, "test")),
            CancellationToken.None);

        Assert.Equal("proposal", result.NormalizedPayload["crm"]!["stage"]!.GetValue<string>());
    }

    [Fact]
    public async Task SqlConnector_RequiresExplicitMode_InsteadOfDefaultingToCustomerOpsReference()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("sqlDatabase");

        var result = await plugin.ValidateConfigurationAsync(
            new ConnectorConfigurationValidationRequest(
                "sqlDatabase",
                DataSourceKind.SqlMetric,
                new JsonObject
                {
                    ["tableName"] = "context_metrics",
                    ["userIdColumn"] = "external_user_id",
                    ["columns"] = new JsonArray("health_score")
                },
                new JsonObject()),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("mode is required", StringComparison.OrdinalIgnoreCase));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddHttpClient("scout-connectors");
        services.AddDbContext<ScoutDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddDbContext<CustomerOpsDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddScoped<ICustomerOpsDbContext>(provider => provider.GetRequiredService<CustomerOpsDbContext>());
        services.AddSingleton<IClock>(new TestClock());
        services.AddScoped<IConnectorPlugin, MockConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, RestApiConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, SqlConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, CsvUploadConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, MockCrmConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, MockBillingConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, MockSupportConnectorPlugin>();
        services.AddScoped<IConnectorRegistry, ConnectorRegistry>();
        services.AddScoped<IConnectorCredentialStore, ProtectedConnectorCredentialStore>();
        return services;
    }

    private static readonly string[] RequiredPublicCatalogueTypes =
    [
        "sqlDatabase",
        "postgresql",
        "sqlServer",
        "restApi",
        "csvUpload",
        "mockCrm",
        "hubspot",
        "salesforce",
        "dynamics",
        "sharepoint",
        "microsoft365-outlook",
        "gmail",
        "zendesk",
        "netsuite",
        "billing-system",
        "productTelemetryEvents",
        "firstPartyConversionEvents",
        "legacy-dotnet-handlers"
    ];

    private static readonly string[] PrivatePlaceholderTypes =
    [
        "sqlServer",
        "billing-system",
        "legacy-dotnet-handlers",
        "salesforce",
        "hubspot",
        "dynamics",
        "snowflake",
        "bigquery",
        "zendesk",
        "netsuite",
        "microsoft365-outlook",
        "gmail",
        "slack",
        "microsoft-teams",
        "outlook-calendar",
        "google-calendar",
        "segment",
        "amplitude",
        "mixpanel",
        "posthog",
        "jira",
        "linear",
        "confluence",
        "notion",
        "sharepoint",
        "google-drive"
    ];

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 05, 11, 12, 0, 0, DateTimeKind.Utc);
    }
}
