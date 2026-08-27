using System.Text.Json;
using KynticAI.Scout.Application;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.AI;
using KynticAI.Scout.Infrastructure.Auth;
using KynticAI.Scout.Infrastructure.Configuration;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Jobs;
using KynticAI.Scout.Infrastructure.Persistence;
using KynticAI.Scout.Infrastructure.ReferenceData;
using KynticAI.Scout.Infrastructure.Selectors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KynticAI.Scout.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class SelectorPipelineIntegrationTests
{
    [Fact]
    public async Task ContextRecomputeProcessor_ResolvesHigherPrioritySelector_WhenMultipleSelectorsTargetSameAttribute()
    {
        await using var harness = await IntegrationHarness.CreateInMemoryAsync();
        var seed = await harness.SeedConflictScenarioAsync();

        var correlationId = "conflict-" + Guid.NewGuid().ToString("N");
        var executions = new[]
        {
            SelectorExecution.Create(seed.Tenant.Id, seed.LowPrioritySelector.Id, seed.UserProfile.Id, correlationId, "test", SelectorExecutionMode.Live, harness.Clock.UtcNow),
            SelectorExecution.Create(seed.Tenant.Id, seed.HighPrioritySelector.Id, seed.UserProfile.Id, correlationId, "test", SelectorExecutionMode.Live, harness.Clock.UtcNow)
        };

        harness.DbContext.SelectorExecutions.AddRange(executions);
        await harness.DbContext.SaveChangesAsync();

        await harness.Processor.ProcessAsync(
            new ContextRecomputeRequest(seed.Tenant.Id, seed.UserProfile.Id, correlationId, executions.Select(x => x.Id).ToList()),
            CancellationToken.None);

        var snapshot = await harness.DbContext.ContextSnapshots
            .Include(x => x.Facts)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .FirstAsync();

        Assert.Single(snapshot.Facts);
        Assert.Equal("\"email\"", snapshot.Facts.Single().ValueJson);
        Assert.Contains("conflictResolution", snapshot.Facts.Single().ProvenanceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserContextLookup_ReportsStaleSnapshot_WhenFactFreshnessHasExpired()
    {
        await using var harness = await IntegrationHarness.CreateInMemoryAsync();
        var seed = await harness.SeedStaleScenarioAsync();

        var correlationId = "stale-" + Guid.NewGuid().ToString("N");
        var execution = SelectorExecution.Create(seed.Tenant.Id, seed.Selector.Id, seed.UserProfile.Id, correlationId, "test", SelectorExecutionMode.Live, harness.Clock.UtcNow);
        harness.DbContext.SelectorExecutions.Add(execution);
        await harness.DbContext.SaveChangesAsync();

        await harness.Processor.ProcessAsync(
            new ContextRecomputeRequest(seed.Tenant.Id, seed.UserProfile.Id, correlationId, new[] { execution.Id }),
            CancellationToken.None);

        var context = await harness.Service.GetUserContextAsync(new UserContextLookupInput(seed.Tenant.Slug, seed.UserProfile.ExternalUserId), CancellationToken.None);

        Assert.NotNull(context);
        Assert.True(context!.IsStale);
        Assert.Single(context.Facts);
        Assert.True(context.Facts.Single().FreshUntilUtc < harness.Clock.UtcNow);
    }

    [Fact]
    public async Task ScheduledDispatcher_QueuesDueUser_WhenLatestSnapshotContainsExpiredFacts()
    {
        await using var harness = await IntegrationHarness.CreateInMemoryAsync();
        var seed = await harness.SeedStaleScenarioAsync();

        var correlationId = "stale-dispatch-" + Guid.NewGuid().ToString("N");
        var initialExecution = SelectorExecution.Create(seed.Tenant.Id, seed.Selector.Id, seed.UserProfile.Id, correlationId, "test", SelectorExecutionMode.Live, harness.Clock.UtcNow);
        harness.DbContext.SelectorExecutions.Add(initialExecution);
        await harness.DbContext.SaveChangesAsync();

        await harness.Processor.ProcessAsync(
            new ContextRecomputeRequest(seed.Tenant.Id, seed.UserProfile.Id, correlationId, new[] { initialExecution.Id }),
            CancellationToken.None);

        var result = await harness.Dispatcher.DispatchDueUsersAsync(seed.Tenant.Slug, CancellationToken.None);

        Assert.Equal(1, result.QueuedUserCount);
        Assert.True(await harness.DbContext.SelectorExecutions.AnyAsync(x => x.ExecutionMode == SelectorExecutionMode.Scheduled));
    }

    [Fact]
    public async Task SqlTableConnector_ReadsCurrentDatabaseRow_AndProducesContextSnapshot()
    {
        await using var harness = await IntegrationHarness.CreateSqliteAsync();
        await harness.DbContext.Database.ExecuteSqlRawAsync("""
            create table customer_metrics (
                external_user_id text not null,
                observed_at_utc text not null,
                preferred_channel text not null
            );
            """);
        await harness.DbContext.Database.ExecuteSqlRawAsync(
            "insert into customer_metrics (external_user_id, observed_at_utc, preferred_channel) values ({0}, {1}, {2});",
            "123",
            harness.Clock.UtcNow.AddMinutes(-15).ToString("O"),
            "email");

        var tenant = Tenant.Create("demo", "Demo", harness.Clock.UtcNow);
        var userProfile = UserProfile.Create(tenant.Id, "123", "Avery Stone", "avery@example.com", "Northstar", "VP RevOps", "enterprise", harness.Clock.UtcNow, harness.Clock.UtcNow);
        var dataSource = DataSource.Create(tenant.Id, "Warehouse Table", "sql table", DataSourceKind.SqlMetric, JsonSerializer.Serialize(new
        {
            connectorType = "sqlTable",
            mode = "currentDatabase",
            tableName = "customer_metrics",
            userIdColumn = "external_user_id",
            observedAtColumn = "observed_at_utc",
            columns = new[] { "preferred_channel" }
        }), harness.Clock.UtcNow);
        var attribute = SemanticAttributeDefinition.Create(tenant.Id, "preferredChannel", "Preferred Channel", "test", SemanticDataType.Enum, "\"email\"", false, harness.Clock.UtcNow);
        var selector = SelectorDefinition.Create(
            tenant.Id,
            dataSource.Id,
            attribute.Id,
            "SQL Preferred Channel",
            "Reads preferred channel from a SQL table.",
            SelectorMappingKind.DirectFieldMapping,
            JsonSerializer.Serialize(new
            {
                rule = new
                {
                    valuePath = "preferred_channel"
                }
            }),
            "Preferred channel {{sourceValue}}.",
            JsonSerializer.Serialize(new
            {
                requiredPaths = new[] { "preferred_channel" }
            }),
            0.95m,
            60,
            5,
            30,
            harness.Clock.UtcNow);

        selector.Publish(harness.Clock.UtcNow);
        harness.DbContext.AddRange(tenant, userProfile, dataSource, attribute, selector);
        await harness.DbContext.SaveChangesAsync();

        var outcome = await harness.Engine.ExecuteAsync(new SelectorRuntimeContext(selector, dataSource, attribute), userProfile, SelectorExecutionMode.Preview, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("\"email\"", outcome.CandidateFact!.ValueJson);
        Assert.Contains("sqlTable", outcome.CandidateFact.ProvenanceJson, StringComparison.Ordinal);
        Assert.Equal(harness.Clock.UtcNow.AddMinutes(-15), outcome.CandidateFact.ObservedAtUtc.ToUniversalTime());

        var correlationId = "sql-connector-" + Guid.NewGuid().ToString("N");
        var execution = SelectorExecution.Create(tenant.Id, selector.Id, userProfile.Id, correlationId, "integration-test", SelectorExecutionMode.Live, harness.Clock.UtcNow);
        harness.DbContext.SelectorExecutions.Add(execution);
        await harness.DbContext.SaveChangesAsync();

        await harness.Processor.ProcessAsync(
            new ContextRecomputeRequest(tenant.Id, userProfile.Id, correlationId, new[] { execution.Id }),
            CancellationToken.None);

        var context = await harness.Service.GetUserContextAsync(new UserContextLookupInput(tenant.Slug, userProfile.ExternalUserId), CancellationToken.None);
        var storedExecution = await harness.DbContext.SelectorExecutions.FirstAsync(x => x.Id == execution.Id);
        var provenance = await harness.DbContext.ProvenanceMetadata.ToListAsync();
        var auditEvent = await harness.DbContext.AuditEvents.FirstOrDefaultAsync(x => x.Action == "context.recompute.completed");

        Assert.NotNull(context);
        Assert.False(context!.IsStale);
        Assert.Equal(1, context.History.Single().SnapshotVersion);
        Assert.Single(context.Facts);
        Assert.Equal("preferredChannel", context.Facts.Single().AttributeKey);
        Assert.Equal("\"email\"", context.Facts.Single().ValueJson);
        Assert.Equal(0.95m, context.Facts.Single().Confidence);
        Assert.Contains("sqlTable", context.Facts.Single().ProvenanceJson, StringComparison.Ordinal);
        Assert.Contains("preferred_channel", storedExecution.RawSourceDataJson, StringComparison.Ordinal);
        Assert.Contains(provenance, x => x.Kind == "selector-execution" && x.SourceSystem == "sqlTable");
        Assert.Contains(provenance, x => x.Kind == "context-fact" && x.SourceSystem == "sqlTable");
        Assert.NotNull(auditEvent);
    }

    [Fact]
    public async Task GetSalesContextPackage_ReturnsGroundedFacts_WithWeakSignals()
    {
        await using var harness = await IntegrationHarness.CreateInMemoryAsync();
        var seed = await harness.SeedSalesSupportScenarioAsync();

        var contextPackage = await harness.Service.GetSalesContextPackageAsync(
            new SalesContextPackageInput(seed.Tenant.Slug, seed.UserProfile.ExternalUserId, "Book a discovery call for enterprise rollout."),
            CancellationToken.None);

        Assert.NotNull(contextPackage);
        Assert.Equal("123", contextPackage!.ExternalUserId);
        Assert.Equal(5, contextPackage.Facts.Count);
        Assert.True(contextPackage.HumanReviewRecommended);
        Assert.Contains(contextPackage.WeakSignalMessages, message => message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(contextPackage.WeakSignalMessages, message => message.Contains("low confidence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"citationId\":\"FACT-01\"", contextPackage.ContextPackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAgentRunAsync_DoesNotExecuteAIModels_AndPersistsNothing()
    {
        await using var harness = await IntegrationHarness.CreateInMemoryAsync();
        var seed = await harness.SeedSalesSupportScenarioAsync();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            harness.Service.CreateAgentRunAsync(
                new CreateAgentRunInput(
                    seed.Tenant.Slug,
                    seed.UserProfile.ExternalUserId,
                    seed.PromptTemplate.Id,
                    "gpt-5.5",
                    "Generate an outreach plan for enterprise upsell.",
                    null),
                CancellationToken.None));

        Assert.Contains("Scout core does not execute AI models", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await harness.DbContext.AgentRuns.CountAsync());
        Assert.Equal(0, await harness.DbContext.AuditEvents.CountAsync(x => x.Action == "agent-run.completed"));
    }

    private sealed class IntegrationHarness : IAsyncDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly AsyncServiceScope scope;
        private readonly SqliteConnection? sqliteConnection;
        private readonly SqliteConnection? customerOpsSqliteConnection;

        private IntegrationHarness(
            ServiceProvider serviceProvider,
            AsyncServiceScope scope,
            TestClock clock,
            SqliteConnection? sqliteConnection,
            SqliteConnection? customerOpsSqliteConnection)
        {
            this.serviceProvider = serviceProvider;
            this.scope = scope;
            this.sqliteConnection = sqliteConnection;
            this.customerOpsSqliteConnection = customerOpsSqliteConnection;
            Clock = clock;
            DbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
            CustomerOpsDbContext = scope.ServiceProvider.GetRequiredService<CustomerOpsDbContext>();
            Processor = scope.ServiceProvider.GetRequiredService<ContextRecomputeProcessor>();
            Dispatcher = scope.ServiceProvider.GetRequiredService<IScheduledRecomputeDispatcher>();
            Service = scope.ServiceProvider.GetRequiredService<IScoutService>();
            Engine = scope.ServiceProvider.GetRequiredService<ISelectorExecutionEngine>();
        }

        public TestClock Clock { get; }

        public ScoutDbContext DbContext { get; }

        public CustomerOpsDbContext CustomerOpsDbContext { get; }

        public ContextRecomputeProcessor Processor { get; }

        public IScheduledRecomputeDispatcher Dispatcher { get; }

        public IScoutService Service { get; }

        public ISelectorExecutionEngine Engine { get; }

        public static async Task<IntegrationHarness> CreateInMemoryAsync()
        {
            var services = new ServiceCollection();
            services.AddDbContext<ScoutDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
            services.AddDbContext<CustomerOpsDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
            return await CreateAsync(services, null, null);
        }

        public static async Task<IntegrationHarness> CreateSqliteAsync()
        {
            var services = new ServiceCollection();
            var connection = new SqliteConnection("Data Source=:memory:");
            var customerOpsConnection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await customerOpsConnection.OpenAsync();
            services.AddDbContext<ScoutDbContext>(options => options.UseSqlite(connection));
            services.AddDbContext<CustomerOpsDbContext>(options => options.UseSqlite(customerOpsConnection));
            var harness = await CreateAsync(services, connection, customerOpsConnection);
            await harness.DbContext.Database.EnsureCreatedAsync();
            await harness.CustomerOpsDbContext.Database.EnsureCreatedAsync();
            return harness;
        }

        private static async Task<IntegrationHarness> CreateAsync(
            ServiceCollection services,
            SqliteConnection? connection,
            SqliteConnection? customerOpsConnection)
        {
            var clock = new TestClock(new DateTime(2026, 05, 09, 12, 00, 00, DateTimeKind.Utc));
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
            services.AddSingleton<IClock>(clock);
            services.AddDataProtection();
            services.AddHttpClient("scout-connectors");
            services.AddScoped<IScoutDbContext>(provider => provider.GetRequiredService<ScoutDbContext>());
            services.AddScoped<ICustomerOpsDbContext>(provider => provider.GetRequiredService<CustomerOpsDbContext>());
            services.AddScoped<IOperationalReferenceDataProvider, CustomerOpsOperationalReferenceDataProvider>();
            services.AddScoped<ISelectorExecutionEngine, SelectorExecutionEngine>();
            services.AddScoped<IScheduledRecomputeDispatcher, ScheduledRecomputeDispatcher>();
            services.AddScoped<IStructuredLlmClient, MockStructuredLlmClient>();
            services.AddScoped<IStructuredLlmClientRegistry, StructuredLlmClientRegistry>();
            services.AddScoped<ISalesSupportAgentService, SalesSupportAgentService>();
            services.AddScoped<ContextRecomputeProcessor>();
            services.AddScoped<IConnectorPlugin, MockConnectorPlugin>();
            services.AddScoped<IConnectorPlugin, RestApiConnectorPlugin>();
            services.AddScoped<IConnectorPlugin, SqlConnectorPlugin>();
            services.AddScoped<IConnectorRegistry, ConnectorRegistry>();
            services.AddScoped<IConnectorCredentialStore, ProtectedConnectorCredentialStore>();
            services.AddSingleton<IBackgroundJobMonitor, InMemoryBackgroundJobMonitor>();
            services.AddSingleton<ContextRecomputeQueue>();
            services.AddSingleton<IContextRecomputeQueue>(provider => provider.GetRequiredService<ContextRecomputeQueue>());
            services.AddSingleton<ICurrentActorService>(new TestCurrentActorService(ActorContext.System()));
            services.AddScoped<IPlatformRuntimeOptions>(_ => new TestPlatformRuntimeOptions("BackendOnly", "open-core-apis"));
            services.AddSingleton<IOptions<LlmOptions>>(Options.Create(new LlmOptions
            {
                DefaultProvider = "mock",
                DefaultModel = "gpt-5.5",
                MaxAttempts = 2,
                LowConfidenceThreshold = 0.75m,
                MinimumStrongFacts = 3
            }));
            services.AddSingleton<IOptions<ContextPackageOptions>>(Options.Create(new ContextPackageOptions()));
            services.AddScoutApplication();

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var harness = new IntegrationHarness(provider, scope, clock, connection, customerOpsConnection);
            await harness.DbContext.Database.EnsureCreatedAsync();
            await harness.CustomerOpsDbContext.Database.EnsureCreatedAsync();
            return harness;
        }

        public async Task<(Tenant Tenant, UserProfile UserProfile, SelectorDefinition LowPrioritySelector, SelectorDefinition HighPrioritySelector)> SeedConflictScenarioAsync()
        {
            var tenant = Tenant.Create("demo", "Demo", Clock.UtcNow);
            var userProfile = UserProfile.Create(tenant.Id, "123", "Avery Stone", "avery@example.com", "Northstar", "VP RevOps", "enterprise", Clock.UtcNow, Clock.UtcNow);
            var attribute = SemanticAttributeDefinition.Create(tenant.Id, "preferredChannel", "Preferred Channel", "test", SemanticDataType.Enum, "\"email\"", false, Clock.UtcNow);
            var crm = DataSource.Create(tenant.Id, "CRM", "crm", DataSourceKind.Crm, JsonSerializer.Serialize(new { connectorType = "mockSignal" }), Clock.UtcNow);
            var support = DataSource.Create(tenant.Id, "Support", "support", DataSourceKind.Crm, JsonSerializer.Serialize(new { connectorType = "mockSignal" }), Clock.UtcNow);
            var lowPrioritySelector = SelectorDefinition.Create(
                tenant.Id,
                crm.Id,
                attribute.Id,
                "Low Priority Channel",
                "low",
                SelectorMappingKind.DirectFieldMapping,
                JsonSerializer.Serialize(new
                {
                    rule = new
                    {
                        valuePath = "crm.preferredChannel"
                    }
                }),
                "CRM channel {{sourceValue}}.",
                JsonSerializer.Serialize(new
                {
                    requiredPaths = new[] { "crm.preferredChannel" }
                }),
                0.98m,
                60,
                1,
                30,
                Clock.UtcNow);
            var highPrioritySelector = SelectorDefinition.Create(
                tenant.Id,
                support.Id,
                attribute.Id,
                "High Priority Channel",
                "high",
                SelectorMappingKind.DirectFieldMapping,
                JsonSerializer.Serialize(new
                {
                    rule = new
                    {
                        valuePath = "support.preferredChannel"
                    }
                }),
                "Support channel {{sourceValue}}.",
                JsonSerializer.Serialize(new
                {
                    requiredPaths = new[] { "support.preferredChannel" }
                }),
                0.82m,
                60,
                5,
                30,
                Clock.UtcNow);

            lowPrioritySelector.Publish(Clock.UtcNow);
            highPrioritySelector.Publish(Clock.UtcNow);

            DbContext.AddRange(tenant, userProfile, attribute, crm, support, lowPrioritySelector, highPrioritySelector);
            DbContext.UserSignals.AddRange(
                UserSignal.Create(tenant.Id, userProfile.Id, crm.Id, "crm.preferredChannel", JsonSerializer.Serialize("phone"), FactValueType.Enum, Clock.UtcNow.AddMinutes(-5), "[]", Clock.UtcNow),
                UserSignal.Create(tenant.Id, userProfile.Id, support.Id, "support.preferredChannel", JsonSerializer.Serialize("email"), FactValueType.Enum, Clock.UtcNow.AddMinutes(-10), "[]", Clock.UtcNow));
            await DbContext.SaveChangesAsync();

            return (tenant, userProfile, lowPrioritySelector, highPrioritySelector);
        }

        public async Task<(Tenant Tenant, UserProfile UserProfile, SelectorDefinition Selector)> SeedStaleScenarioAsync()
        {
            var tenant = Tenant.Create("demo", "Demo", Clock.UtcNow);
            var userProfile = UserProfile.Create(tenant.Id, "123", "Avery Stone", "avery@example.com", "Northstar", "VP RevOps", "enterprise", Clock.UtcNow, Clock.UtcNow);
            var attribute = SemanticAttributeDefinition.Create(tenant.Id, "conversionProbability", "Conversion Probability", "test", SemanticDataType.Percentage, "80", false, Clock.UtcNow);
            var dataSource = DataSource.Create(tenant.Id, "Warehouse", "warehouse", DataSourceKind.SqlMetric, JsonSerializer.Serialize(new { connectorType = "mockSignal" }), Clock.UtcNow);
            var selector = SelectorDefinition.Create(
                tenant.Id,
                dataSource.Id,
                attribute.Id,
                "Stale Conversion Score",
                "stale",
                SelectorMappingKind.DirectFieldMapping,
                JsonSerializer.Serialize(new
                {
                    rule = new
                    {
                        valuePath = "warehouse.conversionProbability"
                    }
                }),
                "Conversion {{sourceValue}}.",
                JsonSerializer.Serialize(new
                {
                    requiredPaths = new[] { "warehouse.conversionProbability" }
                }),
                0.9m,
                5,
                2,
                5,
                Clock.UtcNow);
            selector.Publish(Clock.UtcNow);

            DbContext.AddRange(tenant, userProfile, attribute, dataSource, selector);
            DbContext.UserSignals.Add(
                UserSignal.Create(tenant.Id, userProfile.Id, dataSource.Id, "warehouse.conversionProbability", JsonSerializer.Serialize(80), FactValueType.Number, Clock.UtcNow.AddHours(-2), "[]", Clock.UtcNow));
            await DbContext.SaveChangesAsync();

            return (tenant, userProfile, selector);
        }

        public async Task<(Tenant Tenant, UserProfile UserProfile, PromptTemplate PromptTemplate, ContextSnapshot Snapshot)> SeedSalesSupportScenarioAsync()
        {
            var tenant = Tenant.Create("demo", "Demo", Clock.UtcNow);
            var userProfile = UserProfile.Create(tenant.Id, "123", "Avery Stone", "avery@example.com", "Northstar", "VP RevOps", "enterprise", Clock.UtcNow, Clock.UtcNow);
            var dataSource = DataSource.Create(tenant.Id, "Sales Signals", "sales", DataSourceKind.Crm, JsonSerializer.Serialize(new { connectorType = "mockPayload" }), Clock.UtcNow);

            var attributes = new[]
            {
                SemanticAttributeDefinition.Create(tenant.Id, "conversionProbability", "Conversion Probability", "score", SemanticDataType.Percentage, "85", false, Clock.UtcNow),
                SemanticAttributeDefinition.Create(tenant.Id, "preferredChannel", "Preferred Channel", "channel", SemanticDataType.Enum, "\"email\"", false, Clock.UtcNow),
                SemanticAttributeDefinition.Create(tenant.Id, "planInterest", "Plan Interest", "plan", SemanticDataType.Enum, "\"enterprise\"", false, Clock.UtcNow),
                SemanticAttributeDefinition.Create(tenant.Id, "churnRisk", "Churn Risk", "risk", SemanticDataType.Percentage, "12", false, Clock.UtcNow),
                SemanticAttributeDefinition.Create(tenant.Id, "engagementLevel", "Engagement Level", "engagement", SemanticDataType.Enum, "\"high\"", false, Clock.UtcNow)
            };

            var selectors = attributes.Select((attribute, index) =>
            {
                var selector = SelectorDefinition.Create(
                    tenant.Id,
                    dataSource.Id,
                    attribute.Id,
                    $"{attribute.DisplayName} Selector",
                    "seed",
                    SelectorMappingKind.DirectFieldMapping,
                    JsonSerializer.Serialize(new { rule = new { valuePath = attribute.Key } }),
                    $"{attribute.DisplayName} resolved from seeded data.",
                    JsonSerializer.Serialize(new { requiredPaths = new[] { attribute.Key } }),
                    0.9m,
                    60,
                    index + 1,
                    30,
                    Clock.UtcNow);
                selector.Publish(Clock.UtcNow);
                return selector;
            }).ToArray();

            var promptTemplate = PromptTemplate.Create(
                tenant.Id,
                "Intelligent Sales Support v1",
                "Grounded sales planning prompt.",
                "You are a sales support copilot. Only use the grounded context package provided to you.",
                "Cite context facts, call out low-confidence or stale evidence, never invent missing details, and recommend human review when the package is weak.",
                "Build a sales recommendation for {{user.fullName}} at {{user.companyName}} to accomplish {{salesObjective}}.",
                """
                {
                  "type": "object",
                  "required": [
                    "salesObjective",
                    "outreachStrategy",
                    "personalizedEmailDraft",
                    "followUpRecommendations",
                    "missingInformation",
                    "humanReviewRecommended",
                    "humanReviewReason",
                    "overallConfidence"
                  ]
                }
                """,
                """
                [
                  "Cite grounded facts using citation ids.",
                  "Do not invent details that are not present in the context package.",
                  "Acknowledge low-confidence or stale signals explicitly.",
                  "Recommend human review when evidence is weak or incomplete."
                ]
                """,
                Clock.UtcNow);

            var snapshot = ContextSnapshot.Create(
                tenant.Id,
                userProfile.Id,
                1,
                "85% conversion probability, prefers email, interested in Enterprise plans, churn risk is 12%, engagement is high.",
                0.84m,
                Clock.UtcNow.AddMinutes(-20));

            var facts = new[]
            {
                ContextFact.Create(tenant.Id, snapshot.Id, attributes[0].Id, selectors[0].Id, "conversionProbability", "85", FactValueType.Number, 0.93m, Clock.UtcNow.AddMinutes(-25), Clock.UtcNow.AddMinutes(35), "Conversion probability is high based on current opportunity signals.", """[{"source":"crm","field":"conversionProbability"}]""", Clock.UtcNow),
                ContextFact.Create(tenant.Id, snapshot.Id, attributes[1].Id, selectors[1].Id, "preferredChannel", "\"email\"", FactValueType.Enum, 0.97m, Clock.UtcNow.AddMinutes(-30), Clock.UtcNow.AddMinutes(30), "Email is the strongest recorded contact preference.", """[{"source":"crm","field":"preferredChannel"}]""", Clock.UtcNow),
                ContextFact.Create(tenant.Id, snapshot.Id, attributes[2].Id, selectors[2].Id, "planInterest", "\"enterprise\"", FactValueType.Enum, 0.92m, Clock.UtcNow.AddMinutes(-28), Clock.UtcNow.AddMinutes(32), "The latest commercial intent points to Enterprise plan interest.", """[{"source":"crm","field":"planInterest"}]""", Clock.UtcNow),
                ContextFact.Create(tenant.Id, snapshot.Id, attributes[3].Id, selectors[3].Id, "churnRisk", "12", FactValueType.Number, 0.61m, Clock.UtcNow.AddMinutes(-27), Clock.UtcNow.AddMinutes(33), "Churn risk is directionally useful but low confidence.", """[{"source":"warehouse","field":"churnRisk"}]""", Clock.UtcNow),
                ContextFact.Create(tenant.Id, snapshot.Id, attributes[4].Id, selectors[4].Id, "engagementLevel", "\"high\"", FactValueType.Enum, 0.88m, Clock.UtcNow.AddHours(-3), Clock.UtcNow.AddMinutes(-15), "Recent product activity was high, but the signal is now stale.", """[{"source":"product","field":"engagementLevel"}]""", Clock.UtcNow)
            };

            DbContext.AddRange(tenant, userProfile, dataSource, promptTemplate, snapshot);
            DbContext.SemanticAttributeDefinitions.AddRange(attributes);
            DbContext.SelectorDefinitions.AddRange(selectors);
            DbContext.ContextFacts.AddRange(facts);
            await DbContext.SaveChangesAsync();

            return (tenant, userProfile, promptTemplate, snapshot);
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await serviceProvider.DisposeAsync();
            if (sqliteConnection is not null)
            {
                await sqliteConnection.DisposeAsync();
            }

            if (customerOpsSqliteConnection is not null)
            {
                await customerOpsSqliteConnection.DisposeAsync();
            }
        }
    }

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    private sealed class TestCurrentActorService(ActorContext actorContext) : ICurrentActorService
    {
        public ActorContext GetCurrentActor() => actorContext;
    }

    private sealed class TestPlatformRuntimeOptions(string mode, params string[] featureFlags) : IPlatformRuntimeOptions
    {
        public string Mode => mode;

        public IReadOnlyList<string> EnabledFeatureFlags => featureFlags;
    }
}
