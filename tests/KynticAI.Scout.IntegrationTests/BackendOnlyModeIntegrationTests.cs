using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

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
    public async Task MachineClientToken_CanCallRestAndGraphQl_InBackendOnlyMode()
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

        AuthenticateAsMachineClient(client);

        var restResponse = await client.GetAsync("/api/rest/connectors/plugins");
        Assert.Equal(HttpStatusCode.OK, restResponse.StatusCode);

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query MachineClientPlugins {
                  connectorPlugins {
                    connectorType
                  }
                }
                """
        });
        Assert.Equal(HttpStatusCode.OK, graphQlResponse.StatusCode);
        var graphQlPayload = JsonNode.Parse(await graphQlResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(graphQlPayload["data"]?["connectorPlugins"]?.AsArray().Count > 0);
    }

    private static void AuthenticateAsMachineClient(HttpClient client)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("scout-tests-signing-key-1234567890"));
        var token = new JwtSecurityToken(
            issuer: "KynticAI.Scout.Tests",
            audience: "KynticAI.Scout.Tests",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "client:svc-demo-admin"),
                new Claim(ClaimTypes.NameIdentifier, "client:svc-demo-admin"),
                new Claim("client_id", "svc-demo-admin"),
                new Claim("tenant_slug", "demo"),
                new Claim("display_name", "Demo Service Client"),
                new Claim(ClaimTypes.Email, "svc-demo-admin@machines.scout.local"),
                new Claim(JwtRegisteredClaimNames.Email, "svc-demo-admin@machines.scout.local"),
                new Claim(ClaimTypes.Role, "tenant_admin")
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
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
                    ["Auth:MachineClients:0:Role"] = "tenant_admin",
                    ["Auth:MachineClients:0:Scopes:0"] = "context:read",
                    ["Auth:MachineClients:0:Scopes:1"] = "context:write",
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
