using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Selectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KynticAI.Scout.UnitTests;

public sealed class SelectorExecutionEngineTests
{
    [Fact]
    public async Task DirectFieldMapping_AppliesTransforms_AndMapsValue()
    {
        await using var harness = await SelectorTestHarness.CreateAsync();
        var runtimeContext = harness.CreateRuntimeContext(
            "Preferred Channel",
            SelectorMappingKind.DirectFieldMapping,
            new
            {
                transforms = new[]
                {
                    new
                    {
                        path = "crm.preferredChannel",
                        type = "lower"
                    }
                },
                rule = new
                {
                    valuePath = "crm.preferredChannel"
                }
            },
            new
            {
                requiredPaths = new[] { "crm.preferredChannel" }
            },
            "Resolved preferred channel {{sourceValue}}.",
            new
            {
                connectorType = "mockPayload",
                records = new[]
                {
                    new
                    {
                        externalUserId = harness.UserProfile.ExternalUserId,
                        observedAtUtc = harness.Clock.UtcNow.AddHours(-2),
                        payload = new
                        {
                            crm = new
                            {
                                preferredChannel = "EMAIL"
                            }
                        }
                    }
                }
            });

        var outcome = await harness.Engine.ExecuteAsync(runtimeContext, harness.UserProfile, SelectorExecutionMode.Preview, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.CandidateFact);
        Assert.Equal("\"email\"", outcome.CandidateFact.ValueJson);
        Assert.Equal("Resolved preferred channel email.", outcome.CandidateFact.Explanation);
    }

    [Fact]
    public async Task StringToEnumMapping_MapsConfiguredEnumValue()
    {
        await using var harness = await SelectorTestHarness.CreateAsync();
        var runtimeContext = harness.CreateRuntimeContext(
            "Plan Interest",
            SelectorMappingKind.StringToEnumMapping,
            new
            {
                rule = new
                {
                    valuePath = "crm.planInterest",
                    map = new
                    {
                        enterprise_contacted = "enterprise",
                        growth_eval = "growth"
                    }
                }
            },
            new
            {
                requiredPaths = new[] { "crm.planInterest" }
            },
            "Plan interest {{mappedValue}}.",
            new
            {
                connectorType = "mockPayload",
                records = new[]
                {
                    new
                    {
                        externalUserId = harness.UserProfile.ExternalUserId,
                        observedAtUtc = harness.Clock.UtcNow.AddHours(-1),
                        payload = new
                        {
                            crm = new
                            {
                                planInterest = "enterprise_contacted"
                            }
                        }
                    }
                }
            });

        var outcome = await harness.Engine.ExecuteAsync(runtimeContext, harness.UserProfile, SelectorExecutionMode.Preview, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("\"enterprise\"", outcome.CandidateFact!.ValueJson);
        Assert.Equal(FactValueType.Enum, outcome.CandidateFact.ValueType);
    }

    [Fact]
    public async Task ThresholdClassification_MapsHighBucket()
    {
        await using var harness = await SelectorTestHarness.CreateAsync();
        var runtimeContext = harness.CreateRuntimeContext(
            "Engagement Level",
            SelectorMappingKind.ThresholdClassification,
            new
            {
                rule = new
                {
                    valuePath = "usage.activityScore",
                    thresholds = new object[]
                    {
                        new { min = 80, label = "high" },
                        new { min = 50, max = 80, label = "medium" },
                        new { min = 0, max = 50, label = "low" }
                    }
                }
            },
            new
            {
                requiredPaths = new[] { "usage.activityScore" }
            },
            "Engagement {{classifiedValue}}.",
            new
            {
                connectorType = "mockPayload",
                records = new[]
                {
                    new
                    {
                        externalUserId = harness.UserProfile.ExternalUserId,
                        observedAtUtc = harness.Clock.UtcNow.AddMinutes(-20),
                        payload = new
                        {
                            usage = new
                            {
                                activityScore = 91
                            }
                        }
                    }
                }
            });

        var outcome = await harness.Engine.ExecuteAsync(runtimeContext, harness.UserProfile, SelectorExecutionMode.Preview, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("\"high\"", outcome.CandidateFact!.ValueJson);
    }

    [Fact]
    public async Task WeightedScoring_ComputesExpectedContributionTotal()
    {
        await using var harness = await SelectorTestHarness.CreateAsync();
        var runtimeContext = harness.CreateRuntimeContext(
            "Conversion Probability",
            SelectorMappingKind.WeightedScoring,
            new
            {
                rule = new
                {
                    minimum = 0,
                    maximum = 100,
                    components = new object[]
                    {
                        new
                        {
                            sourcePath = "warehouse.opportunityStage",
                            weight = 1,
                            map = new
                            {
                                proposal = 60,
                                discovery = 35
                            },
                            defaultValue = 20
                        },
                        new
                        {
                            sourcePath = "warehouse.planInterest",
                            weight = 1,
                            expected = "enterprise",
                            trueValue = 10,
                            falseValue = 0
                        },
                        new
                        {
                            sourcePath = "warehouse.activeDays30",
                            weight = 1,
                            threshold = 20,
                            trueValue = 10,
                            falseValue = 0
                        },
                        new
                        {
                            sourcePath = "warehouse.featureEvents7",
                            weight = 1,
                            threshold = 50,
                            trueValue = 5,
                            falseValue = 0
                        }
                    }
                }
            },
            new
            {
                requiredPaths = new[] { "warehouse.opportunityStage", "warehouse.planInterest", "warehouse.activeDays30", "warehouse.featureEvents7" }
            },
            "Weighted score {{weightedScore}}.",
            new
            {
                connectorType = "mockPayload",
                records = new[]
                {
                    new
                    {
                        externalUserId = harness.UserProfile.ExternalUserId,
                        observedAtUtc = harness.Clock.UtcNow.AddMinutes(-5),
                        payload = new
                        {
                            warehouse = new
                            {
                                opportunityStage = "proposal",
                                planInterest = "enterprise",
                                activeDays30 = 26,
                                featureEvents7 = 58
                            }
                        }
                    }
                }
            });

        var outcome = await harness.Engine.ExecuteAsync(runtimeContext, harness.UserProfile, SelectorExecutionMode.Preview, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("85", JsonDocument.Parse(outcome.CandidateFact!.ValueJson).RootElement.GetDecimal().ToString("0"));
    }

    [Fact]
    public async Task FormulaMetric_ComputesExpectedArithmeticValue()
    {
        await using var harness = await SelectorTestHarness.CreateAsync();
        var runtimeContext = harness.CreateRuntimeContext(
            "Churn Risk",
            SelectorMappingKind.FormulaMetric,
            new
            {
                rule = new
                {
                    expression = "15 + support_ticket_score + low_nps_penalty - active_days_credit",
                    variables = new object[]
                    {
                        new
                        {
                            name = "support_ticket_score",
                            sourcePath = "warehouse.supportTickets30",
                            multiplier = 2
                        },
                        new
                        {
                            name = "low_nps_penalty",
                            sourcePath = "warehouse.nps",
                            threshold = 50,
                            trueValue = 0,
                            falseValue = 5
                        },
                        new
                        {
                            name = "active_days_credit",
                            sourcePath = "warehouse.activeDays30",
                            threshold = 20,
                            trueValue = 10,
                            falseValue = 0
                        }
                    }
                }
            },
            new
            {
                requiredPaths = new[] { "warehouse.supportTickets30", "warehouse.nps", "warehouse.activeDays30" }
            },
            "Formula score {{formulaValue}}.",
            new
            {
                connectorType = "mockPayload",
                records = new[]
                {
                    new
                    {
                        externalUserId = harness.UserProfile.ExternalUserId,
                        observedAtUtc = harness.Clock.UtcNow.AddHours(-1),
                        payload = new
                        {
                            warehouse = new
                            {
                                supportTickets30 = 1,
                                nps = 42,
                                activeDays30 = 26
                            }
                        }
                    }
                }
            });

        var outcome = await harness.Engine.ExecuteAsync(runtimeContext, harness.UserProfile, SelectorExecutionMode.Preview, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("12", JsonDocument.Parse(outcome.CandidateFact!.ValueJson).RootElement.GetDecimal().ToString("0"));
    }

    [Fact]
    public async Task ValidateAsync_ReturnsErrors_WhenRequiredPathIsMissing()
    {
        await using var harness = await SelectorTestHarness.CreateAsync();
        var runtimeContext = harness.CreateRuntimeContext(
            "Preview Validation",
            SelectorMappingKind.DirectFieldMapping,
            new
            {
                rule = new
                {
                    valuePath = "crm.preferredChannel"
                }
            },
            new
            {
                requiredPaths = new[] { "crm.preferredChannel" }
            },
            "Value {{sourceValue}}.",
            new
            {
                connectorType = "mockPayload",
                records = new[]
                {
                    new
                    {
                        externalUserId = harness.UserProfile.ExternalUserId,
                        observedAtUtc = harness.Clock.UtcNow,
                        payload = new
                        {
                            crm = new
                            {
                                missing = "email"
                            }
                        }
                    }
                }
            });

        var outcome = await harness.Engine.ValidateAsync(runtimeContext, harness.UserProfile, CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Contains(outcome.ValidationErrors, error => error.Contains("crm.preferredChannel", StringComparison.Ordinal));
    }

    private sealed class SelectorTestHarness : IAsyncDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly AsyncServiceScope scope;

        private SelectorTestHarness(ServiceProvider serviceProvider, AsyncServiceScope scope, TestClock clock, UserProfile userProfile, ISelectorExecutionEngine engine)
        {
            this.serviceProvider = serviceProvider;
            this.scope = scope;
            Clock = clock;
            UserProfile = userProfile;
            Engine = engine;
        }

        public TestClock Clock { get; }

        public UserProfile UserProfile { get; }

        public ISelectorExecutionEngine Engine { get; }

        public static async Task<SelectorTestHarness> CreateAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ScoutDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
            services.AddDbContext<CustomerOpsDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
            services.AddScoped<ICustomerOpsDbContext>(provider => provider.GetRequiredService<CustomerOpsDbContext>());

            var clock = new TestClock(new DateTime(2026, 05, 09, 12, 00, 00, DateTimeKind.Utc));
            services.AddSingleton<IClock>(clock);
            services.AddDataProtection();
            services.AddHttpClient("scout-connectors");
            services.AddScoped<ISelectorExecutionEngine, SelectorExecutionEngine>();
            services.AddScoped<IConnectorPlugin, MockConnectorPlugin>();
            services.AddScoped<IConnectorPlugin, RestApiConnectorPlugin>();
            services.AddScoped<IConnectorPlugin, SqlConnectorPlugin>();
            services.AddScoped<IConnectorRegistry, ConnectorRegistry>();
            services.AddScoped<IConnectorCredentialStore, ProtectedConnectorCredentialStore>();

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var userProfile = UserProfile.Create(Guid.NewGuid(), "123", "Avery Stone", "avery@example.com", "Northstar", "VP RevOps", "enterprise", clock.UtcNow, clock.UtcNow);
            return new SelectorTestHarness(provider, scope, clock, userProfile, scope.ServiceProvider.GetRequiredService<ISelectorExecutionEngine>());
        }

        public SelectorRuntimeContext CreateRuntimeContext(
            string attributeKey,
            SelectorMappingKind mappingKind,
            object expression,
            object validationSchema,
            string explanationTemplate,
            object dataSourceConfig)
        {
            var tenantId = Guid.NewGuid();
            var dataSource = DataSource.Create(tenantId, $"{attributeKey} Source", "test", DataSourceKind.Crm, JsonSerializer.Serialize(dataSourceConfig), Clock.UtcNow);
            var attribute = SemanticAttributeDefinition.Create(tenantId, attributeKey, attributeKey, "test", SemanticDataType.Json, "{}", false, Clock.UtcNow);
            var selector = SelectorDefinition.Create(
                tenantId,
                dataSource.Id,
                attribute.Id,
                $"{attributeKey} Selector",
                "test",
                mappingKind,
                JsonSerializer.Serialize(expression),
                explanationTemplate,
                JsonSerializer.Serialize(validationSchema),
                0.9m,
                60,
                1,
                60,
                Clock.UtcNow);
            return new SelectorRuntimeContext(selector, dataSource, attribute);
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await serviceProvider.DisposeAsync();
        }
    }

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
