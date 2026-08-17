using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using KynticAI.Scout.Infrastructure.Auth;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KynticAI.Scout.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class BackendOnlyModeIntegrationTests
{
    [Fact]
    public async Task BackendOnlyMode_DoesNotSeedDemoData_UnlessExplicitlyEnabled()
    {
        await using var factory = new BackendOnlyWebApplicationFactory(seedDemoData: false);
        using var client = factory.CreateClient();

        var healthResponse = await client.GetAsync("/health");
        var swaggerResponse = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var contextDbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        var customerOpsDbContext = scope.ServiceProvider.GetRequiredService<CustomerOpsDbContext>();

        Assert.Equal(0, await contextDbContext.Tenants.CountAsync());
        Assert.Equal(0, await customerOpsDbContext.CustomerOpsTenants.CountAsync());
    }

    [Fact]
    public async Task MachineClientToken_IsScopeBound_AndCannotInheritConfiguredHumanRole()
    {
        await using var factory = new BackendOnlyWebApplicationFactory(seedDemoData: true);
        using var client = factory.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync("/api/auth/token", new
        {
            grantType = "client_credentials",
            clientId = "svc-demo-admin",
            clientSecret = "SvcSecret123!",
            scope = "context:read context:write"
        });

        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var tokenPayload = JsonNode.Parse(await tokenResponse.Content.ReadAsStringAsync())!.AsObject();
        var accessToken = tokenPayload["accessToken"]!.GetValue<string>();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.Equal("client:svc-demo-admin", token.Subject);
        Assert.Equal("demo", token.Claims.Single(claim => claim.Type == "tenant_slug").Value);
        Assert.Equal("svc-demo-admin", token.Claims.Single(claim => claim.Type == "client_id").Value);
        Assert.Equal(RoleNames.ApiClient, token.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
        Assert.DoesNotContain(token.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "tenant_admin");
        Assert.Equal("context:read context:write", token.Claims.Single(claim => claim.Type == "scope").Value);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var v1Response = await client.GetAsync("/api/v1/workspaces?tenantSlug=demo");
        Assert.Equal(HttpStatusCode.OK, v1Response.StatusCode);

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query MachineClientCatalogue {
                  connectorCatalogue {
                    connectorType
                  }
                }
                """
        });
        Assert.Equal(HttpStatusCode.OK, graphQlResponse.StatusCode);
        var graphQlPayload = JsonNode.Parse(await graphQlResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(graphQlPayload["data"]?["connectorCatalogue"]?.AsArray().Count > 0);

        var legacyRestResponse = await client.GetAsync("/api/rest/connectors/plugins");
        Assert.Equal(HttpStatusCode.Forbidden, legacyRestResponse.StatusCode);
    }

    [Fact]
    public async Task MachineClientToken_MalformedJson_ReturnsBoundedBadRequest()
    {
        await using var factory = new BackendOnlyWebApplicationFactory(seedDemoData: true);
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"grantType\":", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/auth/token", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("missing or malformed", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JsonException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("byte position", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "SvcSecret123!")]
    [InlineData("svc-demo-admin", "")]
    public async Task MachineClientToken_MissingCredentials_ReturnsBadRequest(string clientId, string clientSecret)
    {
        await using var factory = new BackendOnlyWebApplicationFactory(seedDemoData: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new
        {
            grantType = "client_credentials",
            clientId,
            clientSecret
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MachineClientToken_BadCredentials_DoNotExposeConfiguredClientDetails()
    {
        await using var factory = new BackendOnlyWebApplicationFactory(seedDemoData: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new
        {
            grantType = "client_credentials",
            clientId = "unknown-client",
            clientSecret = "wrong-secret"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Client authentication failed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("demo", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("svc-demo-admin", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MachineClientToken_DisallowedScope_FailsClosedWithoutScopePolicyDetails()
    {
        await using var factory = new BackendOnlyWebApplicationFactory(seedDemoData: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new
        {
            grantType = "client_credentials",
            clientId = "svc-demo-admin",
            clientSecret = "SvcSecret123!",
            scope = "admin:manage"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Client authentication failed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Requested scope", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin:manage", body, StringComparison.Ordinal);
    }

    private sealed class BackendOnlyWebApplicationFactory(bool seedDemoData) : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot databaseRoot = new();
        private readonly string databaseName = $"backend-only-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
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
                    ["Auth:MachineClients:0:ClientId"] = "svc-demo-admin",
                    ["Auth:MachineClients:0:ClientSecret"] = "SvcSecret123!",
                    ["Auth:MachineClients:0:TenantSlug"] = "demo",
                    ["Auth:MachineClients:0:DisplayName"] = "Demo Service Client",
                    // Deliberately hostile legacy configuration: machine tokens must ignore this
                    // human role and always be issued as api_client identities.
                    ["Auth:MachineClients:0:Role"] = "tenant_admin",
                    ["Auth:MachineClients:0:Scopes:0"] = "context:read",
                    ["Auth:MachineClients:0:Scopes:1"] = "context:write",
                    ["RateLimits:AuthPermitLimit"] = "100",
                    ["Telemetry:OtlpEndpoint"] = string.Empty
                };

                config.AddInMemoryCollection(settings);
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
                    options.UseInMemoryDatabase(databaseName, databaseRoot));
                services.AddDbContext<CustomerOpsDbContext>(options =>
                    options.UseInMemoryDatabase($"{databaseName}-ops", databaseRoot));
                services.AddScoped<KynticAI.Scout.Application.Abstractions.IScoutDbContext>(provider =>
                    provider.GetRequiredService<ScoutDbContext>());
                services.AddScoped<KynticAI.Scout.Application.Abstractions.ICustomerOpsDbContext>(provider =>
                    provider.GetRequiredService<CustomerOpsDbContext>());

                if (seedDemoData)
                {
                    TestSeedHelper.SeedDemoData(services);
                }
            });
        }
    }
}
