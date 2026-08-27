using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KynticAI.Scout.UnitTests;

public sealed class ConnectorAuthoringTests
{
    [Fact]
    public void TemplateConnector_ImplementsIConnectorPlugin()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<IConnectorRegistry>();

        Assert.True(registry.TryGetPlugin("template", out var plugin));
        Assert.NotNull(plugin);
        Assert.IsAssignableFrom<IConnectorPlugin>(plugin);
        Assert.Equal("template", plugin.ConnectorType);
        Assert.Equal("Template Connector", plugin.DisplayName);
    }

    [Fact]
    public void TemplateConnector_PassesMetadataValidation()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<IConnectorRegistry>();
        var plugin = registry.GetRequiredPlugin("template");

        var result = ConnectorMetadataValidator.Validate(plugin);

        Assert.True(result.IsValid, $"Validation errors: {string.Join("; ", result.Errors)}");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TemplateConnector_ConfigSchemaIsValidJsonSchema()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("template");
        var schema = plugin.GetConfigurationSchema();

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.NotNull(schema["properties"]);
        Assert.NotNull(schema["required"]);
    }

    [Fact]
    public void TemplateConnector_SampleConfigSatisfiesRequiredFields()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("template");
        var schema = plugin.GetConfigurationSchema();
        var sample = plugin.GetSampleConfiguration();
        var required = schema["required"] as JsonArray ?? [];

        foreach (var field in required)
        {
            var fieldName = field!.GetValue<string>();
            Assert.NotNull(sample[fieldName]);
        }
    }

    [Fact]
    public async Task TemplateConnector_ValidatesConfiguration_RejectsEmptyRecords()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("template");

        var result = await plugin.ValidateConfigurationAsync(
            new ConnectorConfigurationValidationRequest(
                "template",
                DataSourceKind.Crm,
                new JsonObject(),
                new JsonObject()),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("records"));
    }

    [Fact]
    public async Task TemplateConnector_ValidatesConfiguration_AcceptsSampleConfig()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("template");

        var result = await plugin.ValidateConfigurationAsync(
            new ConnectorConfigurationValidationRequest(
                "template",
                DataSourceKind.Crm,
                plugin.GetSampleConfiguration(),
                new JsonObject()),
            CancellationToken.None);

        Assert.True(result.IsValid, $"Validation errors: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public async Task TemplateConnector_FetchReturnsPayloadForKnownSubject()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("template");
        var user = UserProfile.Create(Guid.NewGuid(), "123", "Test User", "test@example.com", "TestCo", "Engineer", "demo", DateTime.UtcNow, DateTime.UtcNow);
        var dataSource = DataSource.Create(Guid.NewGuid(), "Template DS", "test", DataSourceKind.Crm, "{}", DateTime.UtcNow);
        var selector = SelectorDefinition.Create(
            dataSource.TenantId, dataSource.Id, Guid.NewGuid(),
            "Test Selector", "test", SelectorMappingKind.DirectFieldMapping,
            """{"rule":{"valuePath":"status"}}""", "Status {{sourceValue}}",
            """{"requiredPaths":["status"]}""", 0.9m, 60, 1, 30, DateTime.UtcNow);

        var result = await plugin.FetchAsync(
            new ConnectorFetchRequest(
                "template", selector, dataSource, user,
                plugin.GetSampleConfiguration(),
                new JsonObject(),
                ConnectorRunMode.Preview,
                new ConnectorExecutionTrigger("Preview", null, "test")),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("active", result.NormalizedPayload["status"]!.GetValue<string>());
        Assert.Equal(85, result.NormalizedPayload["score"]!.GetValue<int>());
    }

    [Fact]
    public void InMemoryInventoryConnector_ImplementsIConnectorPlugin()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<IConnectorRegistry>();

        Assert.True(registry.TryGetPlugin("inMemoryInventory", out var plugin));
        Assert.NotNull(plugin);
        Assert.IsAssignableFrom<IConnectorPlugin>(plugin);
        Assert.Equal("inMemoryInventory", plugin.ConnectorType);
    }

    [Fact]
    public void InMemoryInventoryConnector_AliasResolvesToPlugin()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<IConnectorRegistry>();

        Assert.True(registry.TryGetPlugin("demoInventory", out var plugin));
        Assert.NotNull(plugin);
        Assert.Equal("inMemoryInventory", plugin.ConnectorType);
    }

    [Fact]
    public void InMemoryInventoryConnector_PassesMetadataValidation()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("inMemoryInventory");

        var result = ConnectorMetadataValidator.Validate(plugin);

        Assert.True(result.IsValid, $"Validation errors: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public async Task InMemoryInventoryConnector_FetchReturnsBuiltInDemoData()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("inMemoryInventory");
        var user = UserProfile.Create(Guid.NewGuid(), "123", "Test User", "test@example.com", "TestCo", "Engineer", "demo", DateTime.UtcNow, DateTime.UtcNow);
        var dataSource = DataSource.Create(Guid.NewGuid(), "Inventory DS", "test", DataSourceKind.ProductUsage, "{}", DateTime.UtcNow);
        var selector = SelectorDefinition.Create(
            dataSource.TenantId, dataSource.Id, Guid.NewGuid(),
            "Inventory Selector", "test", SelectorMappingKind.DirectFieldMapping,
            """{"rule":{"valuePath":"inventory.skuCount"}}""", "SKUs {{sourceValue}}",
            """{"requiredPaths":["inventory.skuCount"]}""", 0.9m, 60, 1, 30, DateTime.UtcNow);

        var result = await plugin.FetchAsync(
            new ConnectorFetchRequest(
                "inMemoryInventory", selector, dataSource, user,
                new JsonObject { ["warehouseId"] = "warehouse-north" },
                new JsonObject(),
                ConnectorRunMode.Preview,
                new ConnectorExecutionTrigger("Preview", null, "test")),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.NormalizedPayload["inventory"]);
        Assert.Equal(128, result.NormalizedPayload["inventory"]!["skuCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task InMemoryInventoryConnector_FetchReturnsOverrideRecords()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("inMemoryInventory");
        var user = UserProfile.Create(Guid.NewGuid(), "123", "Test User", "test@example.com", "TestCo", "Engineer", "demo", DateTime.UtcNow, DateTime.UtcNow);
        var dataSource = DataSource.Create(Guid.NewGuid(), "Inventory DS", "test", DataSourceKind.ProductUsage, "{}", DateTime.UtcNow);
        var selector = SelectorDefinition.Create(
            dataSource.TenantId, dataSource.Id, Guid.NewGuid(),
            "Inventory Selector", "test", SelectorMappingKind.DirectFieldMapping,
            """{"rule":{"valuePath":"inventory.skuCount"}}""", "SKUs {{sourceValue}}",
            """{"requiredPaths":["inventory.skuCount"]}""", 0.9m, 60, 1, 30, DateTime.UtcNow);

        var result = await plugin.FetchAsync(
            new ConnectorFetchRequest(
                "inMemoryInventory", selector, dataSource, user,
                plugin.GetSampleConfiguration(),
                new JsonObject(),
                ConnectorRunMode.Preview,
                new ConnectorExecutionTrigger("Preview", null, "test")),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(42, result.NormalizedPayload["inventory"]!["skuCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task InMemoryInventoryConnector_ValidatesConfiguration_RejectsMissingWarehouseId()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var plugin = provider.GetRequiredService<IConnectorRegistry>().GetRequiredPlugin("inMemoryInventory");

        var result = await plugin.ValidateConfigurationAsync(
            new ConnectorConfigurationValidationRequest(
                "inMemoryInventory",
                DataSourceKind.ProductUsage,
                new JsonObject(),
                new JsonObject()),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("warehouseId"));
    }

    [Fact]
    public void MetadataValidator_RejectsPlugin_WithEmptyConnectorType()
    {
        var plugin = new BrokenConnectorStub(connectorType: "", displayName: "Test", description: "Test");

        var result = ConnectorMetadataValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ConnectorType"));
    }

    [Fact]
    public void MetadataValidator_RejectsPlugin_WithInvalidConnectorTypeFormat()
    {
        var plugin = new BrokenConnectorStub(connectorType: "bad-connector", displayName: "Test", description: "Test");

        var result = ConnectorMetadataValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("lowercase letter"));
    }

    [Fact]
    public void MetadataValidator_RejectsPlugin_WithNoSupportedDataSourceKinds()
    {
        var plugin = new BrokenConnectorStub(
            connectorType: "broken",
            displayName: "Broken",
            description: "Broken",
            supportedKinds: []);

        var result = ConnectorMetadataValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SupportedDataSourceKinds"));
    }

    [Fact]
    public void MetadataValidator_RejectsPlugin_WithMissingSampleField()
    {
        var plugin = new BrokenConnectorStub(
            connectorType: "broken",
            displayName: "Broken",
            description: "Broken",
            schemaRequired: new JsonArray("foo"),
            sampleConfig: new JsonObject());

        var result = ConnectorMetadataValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("foo"));
    }

    [Fact]
    public void MetadataValidator_RejectsSchemaRequiredField_NotDeclaredInProperties()
    {
        var plugin = new BrokenConnectorStub(
            connectorType: "broken",
            displayName: "Broken",
            description: "Broken",
            schemaRequired: new JsonArray("foo"),
            sampleConfig: new JsonObject { ["foo"] = "bar" });

        var result = ConnectorMetadataValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("required field \"foo\""));
    }

    [Fact]
    public void MetadataValidator_RejectsRawSecretValues_InSampleConfiguration()
    {
        var plugin = new BrokenConnectorStub(
            connectorType: "broken",
            displayName: "Broken",
            description: "Broken",
            sampleConfig: new JsonObject
            {
                ["credentials"] = new JsonObject
                {
                    ["apiKey"] = "plain-text-key"
                }
            });

        var result = ConnectorMetadataValidator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("secret://"));
    }

    [Fact]
    public void ConnectorContractRules_ValidatePublicIngestEventShape()
    {
        var validEvent = new ConnectorIngestEvent(
            "acmeCrm",
            "deal-123",
            "deal",
            new JsonObject
            {
                ["deal_probability"] = 0.82
            },
            new DateTime(2026, 05, 27, 12, 0, 0, DateTimeKind.Utc));

        var valid = ConnectorContractRules.ValidateIngestEvent(validEvent);

        Assert.True(valid.IsValid, string.Join("; ", valid.Errors));

        var invalid = ConnectorContractRules.ValidateIngestEvent(validEvent with
        {
            SourceId = "",
            RawPayload = new JsonObject(),
            TimestampUtc = new DateTime(2026, 05, 27, 12, 0, 0, DateTimeKind.Local)
        });

        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, e => e.Contains("sourceId"));
        Assert.Contains(invalid.Errors, e => e.Contains("rawPayload"));
        Assert.Contains(invalid.Errors, e => e.Contains("UTC"));
    }

    [Fact]
    public void ConnectorConfigurationDescriptor_ProvidesStablePublicConfigModel()
    {
        var descriptor = new ConnectorConfigurationDescriptor(
            "acmeCrm",
            """{"type":"object","properties":{"endpoint":{"type":"string"}}}""",
            """{"type":"object","properties":{"apiKey":{"type":"string","secret":true}}}""",
            """{"endpoint":"https://api.example.com"}""",
            [
                new ConnectorConfigurationField(
                    "endpoint",
                    ConnectorConfigurationValueType.String,
                    true,
                    "Base URL for the public API.")
            ]);

        Assert.Equal("acmeCrm", descriptor.ConnectorType);
        Assert.Equal("endpoint", descriptor.Fields[0].Name);
        Assert.True(descriptor.Fields[0].IsRequired);
        Assert.False(descriptor.Fields[0].IsSecret);
    }

    [Fact]
    public void AllRegisteredConnectors_PassMetadataValidation()
    {
        using var provider = CreateServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<IConnectorRegistry>();

        foreach (var plugin in registry.GetPlugins())
        {
            var result = ConnectorMetadataValidator.Validate(plugin);
            Assert.True(result.IsValid,
                $"Plugin '{plugin.ConnectorType}' failed metadata validation: {string.Join("; ", result.Errors)}");
        }
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
        services.AddScoped<IConnectorPlugin, InMemoryInventoryConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, TemplateConnectorPlugin>();
        services.AddScoped<IConnectorRegistry, ConnectorRegistry>();
        services.AddScoped<IConnectorCredentialStore, ProtectedConnectorCredentialStore>();
        return services;
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 05, 27, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class BrokenConnectorStub : IConnectorPlugin
    {
        private readonly JsonArray? _schemaRequired;
        private readonly JsonObject? _sampleConfig;
        private readonly IReadOnlyList<DataSourceKind> _supportedKinds;

        public BrokenConnectorStub(
            string connectorType,
            string displayName,
            string description,
            IReadOnlyList<DataSourceKind>? supportedKinds = null,
            JsonArray? schemaRequired = null,
            JsonObject? sampleConfig = null)
        {
            ConnectorType = connectorType;
            DisplayName = displayName;
            Description = description;
            _supportedKinds = supportedKinds ?? [DataSourceKind.Crm];
            _schemaRequired = schemaRequired;
            _sampleConfig = sampleConfig;
        }

        public string ConnectorType { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public IReadOnlyList<string> Aliases => [];
        public IReadOnlyList<DataSourceKind> SupportedDataSourceKinds => _supportedKinds;
        public IReadOnlyList<ConnectorCapability> SupportedCapabilities => [ConnectorCapability.FetchSubject];

        public JsonObject GetConfigurationSchema() => new()
        {
            ["type"] = "object",
            ["required"] = _schemaRequired?.DeepClone() ?? new JsonArray(),
            ["properties"] = new JsonObject()
        };

        public JsonObject GetCredentialSchema() => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        };

        public JsonObject GetSampleConfiguration() => _sampleConfig ?? new JsonObject();

        public Task<ConnectorConfigurationValidationResult> ValidateConfigurationAsync(
            ConnectorConfigurationValidationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ConnectorConfigurationValidationResult(true, [], "{}", "{}"));

        public Task<ConnectorHealthCheckResult> CheckHealthAsync(
            ConnectorHealthCheckRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ConnectorHealthCheckResult(true, "ok", [], "{}", DateTime.UtcNow));

        public Task<ConnectorFetchResult> FetchAsync(
            ConnectorFetchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ConnectorFetchResult("{}", new JsonObject(), "[]", DateTime.UtcNow, null, "{}"));
    }
}
