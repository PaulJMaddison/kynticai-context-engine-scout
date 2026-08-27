using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Persistence;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Selectors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace KynticAI.Scout.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class DualDatabaseDemoIntegrationTests
{
    [Fact]
    public async Task SelectorPipeline_ReadsCustomerOpsRollup_AndWritesContextSnapshot()
    {
        await using var contextConnection = new SqliteConnection("Data Source=:memory:");
        await using var customerOpsConnection = new SqliteConnection("Data Source=:memory:");
        await contextConnection.OpenAsync();
        await customerOpsConnection.OpenAsync();

        var contextOptions = new DbContextOptionsBuilder<ScoutDbContext>()
            .UseSqlite(contextConnection)
            .Options;
        var customerOpsOptions = new DbContextOptionsBuilder<CustomerOpsDbContext>()
            .UseSqlite(customerOpsConnection)
            .Options;

        await using var contextDbContext = new ScoutDbContext(contextOptions);
        await using var customerOpsDbContext = new CustomerOpsDbContext(customerOpsOptions);
        await contextDbContext.Database.EnsureCreatedAsync();
        await customerOpsDbContext.Database.EnsureCreatedAsync();

        var clock = new TestClock(new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc));
        var tenant = Tenant.Create("demo", "Demo", clock.UtcNow);
        var userProfile = UserProfile.Create(tenant.Id, "123", "Avery Stone", "avery@example.com", "Larkspur Logistics Group", "VP Revenue Operations", "enterprise", clock.UtcNow, clock.UtcNow);
        var attribute = SemanticAttributeDefinition.Create(tenant.Id, "planInterest", "Plan Interest", "Commercial intent", SemanticDataType.Enum, "\"enterprise\"", true, clock.UtcNow);
        var dataSource = DataSource.Create(
            tenant.Id,
            "Customer Ops Context Rollups",
            "Reads customer_ops_db rollups.",
            DataSourceKind.SqlMetric,
            JsonSerializer.Serialize(new
            {
                connectorType = "sqlTable",
                mode = "customerOpsDatabase",
                tableName = "customer_context_rollups",
                tenantSlug = "demo",
                tenantSlugColumn = "tenant_slug",
                userIdColumn = "external_user_id",
                observedAtColumn = "observed_at_utc",
                columns = new[] { "tenant_slug", "external_user_id", "plan_interest_signal", "observed_at_utc" }
            }),
            clock.UtcNow);
        var selector = SelectorDefinition.Create(
            tenant.Id,
            dataSource.Id,
            attribute.Id,
            "Plan Interest from Rollup",
            "Maps rollup plan interest.",
            SelectorMappingKind.StringToEnumMapping,
            JsonSerializer.Serialize(new
            {
                rule = new
                {
                    valuePath = "plan_interest_signal",
                    map = new
                    {
                        enterprise = "enterprise",
                        growth = "growth",
                        starter = "starter"
                    }
                }
            }),
            "Plan interest {{mappedValue}}.",
            JsonSerializer.Serialize(new { requiredPaths = new[] { "plan_interest_signal" } }),
            0.92m,
            240,
            10,
            60,
            clock.UtcNow);
        selector.Publish(clock.UtcNow);

        contextDbContext.AddRange(tenant, userProfile, attribute, dataSource, selector);
        await contextDbContext.SaveChangesAsync();

        var opsTenant = CustomerOpsTenant.Create("demo", "Demo", clock.UtcNow);
        customerOpsDbContext.CustomerOpsTenants.Add(opsTenant);
        customerOpsDbContext.CustomerContextRollups.Add(CustomerContextRollup.Create(
            opsTenant.Id,
            "demo",
            "123",
            "enterprise",
            88,
            24,
            10,
            120,
            0.92m,
            91,
            1,
            0,
            8,
            14800m,
            0,
            0,
            28,
            78,
            82,
            true,
            92,
            91,
            84,
            "accelerate_enterprise",
            "deepening",
            90,
            12,
            clock.UtcNow.AddMinutes(-20),
            clock.UtcNow));
        await customerOpsDbContext.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(clock);
        services.AddScoped(_ => contextDbContext);
        services.AddScoped(_ => customerOpsDbContext);
        services.AddDataProtection();
        services.AddHttpClient("scout-connectors");
        services.AddScoped<IConnectorPlugin, MockConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, RestApiConnectorPlugin>();
        services.AddScoped<IConnectorPlugin>(_ => new SqlConnectorPlugin(contextDbContext, customerOpsDbContext));
        services.AddScoped<IConnectorRegistry, ConnectorRegistry>();
        services.AddScoped<IConnectorCredentialStore, ProtectedConnectorCredentialStore>();
        services.AddScoped<ISelectorExecutionEngine, SelectorExecutionEngine>();
        var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<ISelectorExecutionEngine>();

        var executionOutcome = await engine.ExecuteAsync(
            new SelectorRuntimeContext(selector, dataSource, attribute),
            userProfile,
            SelectorExecutionMode.Preview,
            CancellationToken.None);

        Assert.True(executionOutcome.IsSuccess);
        Assert.Equal("\"enterprise\"", executionOutcome.CandidateFact!.ValueJson);
    }

    [Fact]
    public async Task GraphQl_UserContext_ReturnsOperationalSourceSummary_AndSnapshotHistory()
    {
        await using var factory = new DemoWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var payload = await ExecuteGraphQlAsync(client, """
            query GetUserContext {
              userContext(input: { tenantSlug: "demo", externalUserId: "123" }) {
                externalUserId
                sourceSummary {
                  accountName
                  activePlanName
                  pricingPageVisits30d
                }
                history {
                  snapshotVersion
                  factCount
                }
              }
            }
            """);

        Assert.Equal("123", payload["data"]?["userContext"]?["externalUserId"]?.GetValue<string>());
        Assert.Equal("Larkspur Logistics Group", payload["data"]?["userContext"]?["sourceSummary"]?["accountName"]?.GetValue<string>());
        Assert.True(payload["data"]?["userContext"]?["history"]?.AsArray().Count > 0);
    }

    [Fact]
    public async Task GraphQl_CreateAgentRun_DoesNotExecuteAIModels()
    {
        await using var factory = new DemoWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var promptTemplatesPayload = await ExecuteGraphQlAsync(client, """
            query GetPromptTemplates {
              promptTemplates(tenantSlug: "demo") {
                id
              }
            }
            """);
        var promptTemplateId = promptTemplatesPayload["data"]?["promptTemplates"]?[0]?["id"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(promptTemplateId));

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            operationName = "CreateAgentRun",
            query = """
                mutation CreateAgentRun($input: CreateAgentRunInput!) {
                  createAgentRun(input: $input) {
                    status
                    providerName
                    outputJson
                    validationErrorsJson
                  }
                }
                """,
            variables = new
            {
                input = new
                {
                    tenantSlug = "demo",
                    externalUserId = "123",
                    promptTemplateId,
                    modelName = "gpt-5.5",
                    salesObjective = "Book a discovery call for the enterprise rollout in the next seven days.",
                    providerName = "mock"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var errors = payload["errors"]?.AsArray();
        Assert.NotNull(errors);
        var errorMessage = errors![0]?["extensions"]?["exception"]?["message"]?.GetValue<string>()
            ?? errors[0]!["message"]?.GetValue<string>()
            ?? string.Empty;
        Assert.Contains("Scout core does not execute AI models", errorMessage, StringComparison.Ordinal);
        Assert.Null(payload["data"]);
    }

    private static void AuthenticateAs(HttpClient client, string role, string email, string displayName)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("scout-tests-signing-key-1234567890"));
        var token = new JwtSecurityToken(
            issuer: "KynticAI.Scout.Tests",
            audience: "KynticAI.Scout.Tests",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Email, email),
                new Claim("tenant_id", Guid.NewGuid().ToString("D")),
                new Claim("tenant_slug", "demo"),
                new Claim("display_name", displayName),
                new Claim(ClaimTypes.Role, role)
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static async Task<JsonObject> ExecuteGraphQlAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GraphQL request failed with {(int)response.StatusCode}: {content}");
        }

        return JsonNode.Parse(content)!.AsObject();
    }

    private sealed class DemoWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection contextConnection = new("Data Source=:memory:");
        private readonly SqliteConnection customerOpsConnection = new("Data Source=:memory:");

        public DemoWebApplicationFactory()
        {
            contextConnection.Open();
            customerOpsConnection.Open();
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Platform:Mode"] = "BackendOnly",
                    ["Platform:EnableRest"] = "true",
                    ["Platform:EnableGraphQl"] = "true",
                    ["Platform:EnableOpenApi"] = "true",
                    ["Bootstrap:ApplyMigrationsOnStartup"] = "false",
                    ["Bootstrap:SeedDemoData"] = "false",
                    ["Auth:Issuer"] = "KynticAI.Scout.Tests",
                    ["Auth:Audience"] = "KynticAI.Scout.Tests",
                    ["Auth:SigningKey"] = "scout-tests-signing-key-1234567890",
                    ["Auth:AccessTokenMinutes"] = "60",
                    ["Telemetry:OtlpEndpoint"] = string.Empty
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ScoutDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ScoutDbContext>>();
                services.RemoveAll<ScoutDbContext>();
                services.RemoveAll<DbContextOptions<CustomerOpsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CustomerOpsDbContext>>();
                services.RemoveAll<CustomerOpsDbContext>();

                services.AddDbContext<ScoutDbContext>(options =>
                    options.UseSqlite(contextConnection));
                services.AddDbContext<CustomerOpsDbContext>(options =>
                    options.UseSqlite(customerOpsConnection));
                services.AddScoped<IScoutDbContext>(provider => provider.GetRequiredService<ScoutDbContext>());
                services.AddScoped<ICustomerOpsDbContext>(provider => provider.GetRequiredService<CustomerOpsDbContext>());

                TestSeedHelper.SeedDemoData(services);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                contextConnection.Dispose();
                customerOpsConnection.Dispose();
            }
        }
    }

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
