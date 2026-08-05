using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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

namespace KynticAI.Scout.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class CrossTenantRestAuthorizationIntegrationTests
{
    [Fact]
    public async Task TenantAdmin_CannotAccessAnotherTenant_AcrossAdminEndpoints()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        await AssertCrossTenantDeniedAsync(await client.GetAsync("/api/v1/audit-events/export?tenantSlug=summit"));
        await AssertCrossTenantDeniedAsync(await client.GetAsync("/api/v1/admin/organisation?tenantSlug=summit"));
        await AssertCrossTenantDeniedAsync(await client.GetAsync("/api/v1/admin/users?tenantSlug=summit"));
        await AssertCrossTenantDeniedAsync(await client.GetAsync("/api/v1/blueprints?tenantSlug=summit"));
        await AssertCrossTenantDeniedAsync(await client.GetAsync("/api/v1/governance/policies?tenantSlug=summit"));

        var patchResponse = await client.PatchAsJsonAsync(
            $"/api/v1/admin/users/{Guid.NewGuid()}?tenantSlug=summit",
            new { displayName = "X", role = "IntegrationAdmin", isActive = true });
        await AssertCrossTenantDeniedAsync(patchResponse);
    }

    [Fact]
    public async Task TenantAdmin_SameTenantAdminEndpoints_StillWork()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var organisationResponse = await client.GetAsync("/api/v1/admin/organisation?tenantSlug=demo");
        Assert.Equal(HttpStatusCode.OK, organisationResponse.StatusCode);
        var organisation = JsonNode.Parse(await organisationResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("demo", organisation["tenantSlug"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/users?tenantSlug=demo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/blueprints?tenantSlug=demo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/governance/policies?tenantSlug=demo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/audit-events/export?tenantSlug=demo")).StatusCode);

        var usersResponse = await client.GetAsync("/api/v1/admin/users?tenantSlug=demo&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode);
        var usersPayload = JsonNode.Parse(await usersResponse.Content.ReadAsStringAsync())!.AsObject();
        var integrationAdmin = usersPayload["items"]!
            .AsArray()
            .Single(item => item?["email"]?.GetValue<string>() == "integrations@scout.local")!
            .AsObject();
        var userId = integrationAdmin["id"]!.GetValue<Guid>();

        var updateResponse = await client.PatchAsJsonAsync($"/api/v1/admin/users/{userId}?tenantSlug=demo", new
        {
            displayName = "Riley Chen",
            role = "IntegrationAdmin",
            isActive = true
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }

    [Fact]
    public async Task PlatformOwner_MayReadAnotherTenant_WhileTenantAdminMayNot()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "platform_owner", "owner@scout.local", "Pat Quinn");

        var organisationResponse = await client.GetAsync("/api/v1/admin/organisation?tenantSlug=summit");
        Assert.Equal(HttpStatusCode.OK, organisationResponse.StatusCode);
        var organisation = JsonNode.Parse(await organisationResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("summit", organisation["tenantSlug"]!.GetValue<string>());

        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");
        var deniedResponse = await client.GetAsync("/api/v1/admin/organisation?tenantSlug=summit");
        await AssertCrossTenantDeniedAsync(deniedResponse);
    }

    [Fact]
    public async Task CrossTenantRestDenial_IsAudited()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var deniedResponse = await client.GetAsync("/api/v1/admin/organisation?tenantSlug=summit");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        var payload = JsonNode.Parse(await deniedResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("authorization.denied", payload["error"]!["code"]!.GetValue<string>());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        Assert.Contains(await dbContext.AuditEvents.ToListAsync(), audit =>
            audit.Action == "auth.permission.denied");
    }

    private static async Task AssertCrossTenantDeniedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("authorization.denied", payload["error"]!["code"]!.GetValue<string>());
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

    private sealed class ScoutWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"scout-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
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
                    options.UseInMemoryDatabase(_databaseName, _databaseRoot));
                services.AddDbContext<CustomerOpsDbContext>(options =>
                    options.UseInMemoryDatabase($"{_databaseName}-ops", _databaseRoot));
                services.AddScoped<KynticAI.Scout.Application.Abstractions.IScoutDbContext>(provider =>
                    provider.GetRequiredService<ScoutDbContext>());
                services.AddScoped<KynticAI.Scout.Application.Abstractions.ICustomerOpsDbContext>(provider =>
                    provider.GetRequiredService<CustomerOpsDbContext>());

                TestSeedHelper.SeedDemoData(services);
            });
        }
    }
}
