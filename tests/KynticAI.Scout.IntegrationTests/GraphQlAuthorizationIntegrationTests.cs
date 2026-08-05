using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace KynticAI.Scout.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class GraphQlAuthorizationIntegrationTests
{
    [Fact]
    public async Task GraphQl_RejectsAnonymousRequests()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "{ dataSources(tenantSlug: \"demo\") { id } }"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_IsRateLimited_AfterRepeatedFailures()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage? lastResponse = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            lastResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                tenantSlug = "demo",
                email = "rep@scout.local",
                password = "WrongPassword123!"
            });
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
    }

    [Fact]
    public async Task Health_EchoesRequestIdentifier_ForTracing()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Request-Id", "trace-req-123");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Request-Id", out var values));
        Assert.Equal("trace-req-123", values.Single());
    }

    [Fact]
    public async Task SalesRep_CannotUpsertDataSource()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "sales_rep", "rep@scout.local", "Jordan Kim");

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            operationName = "UpsertDataSource",
            query = """
                mutation UpsertDataSource($input: UpsertDataSourceInput!) {
                  upsertDataSource(input: $input) {
                    id
                  }
                }
                """,
            variables = new
            {
                input = new
                {
                    tenantSlug = "demo",
                    name = "Rep Attempt",
                    description = "Should be blocked.",
                    kind = "CRM",
                    connectionConfigJson = "{}"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var errors = payload["errors"]?.AsArray();
        Assert.NotNull(errors);
        Assert.Contains(errors!, error =>
            error?["message"]?.GetValue<string>().Contains("authorized", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task SalesRep_UserProfiles_AreMasked()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "sales_rep", "rep@scout.local", "Jordan Kim");

        var payload = await ExecuteGraphQlAsync(client, """
            query GetUserProfiles {
              userProfiles(tenantSlug: "demo") {
                externalUserId
                email
                isEmailMasked
              }
            }
            """);

        var firstUser = payload["data"]?["userProfiles"]?[0];
        Assert.NotNull(firstUser);
        Assert.True(firstUser!["isEmailMasked"]!.GetValue<bool>());
        Assert.Equal("a***@larkspur-logistics.example", firstUser["email"]!.GetValue<string>());
    }

    [Fact]
    public async Task TenantAdmin_CanReadAuditEvents_AndOpsSummary()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var graphqlPayload = await ExecuteGraphQlAsync(client, """
            query GetAuditEvents {
              auditEvents(tenantSlug: "demo") {
                action
                entityType
              }
            }
            """);

        Assert.NotNull(graphqlPayload["data"]?["auditEvents"]);
        Assert.True(graphqlPayload["data"]!["auditEvents"]!.AsArray().Count > 0);

        var opsResponse = await client.GetAsync("/api/ops/summary");
        Assert.Equal(HttpStatusCode.OK, opsResponse.StatusCode);

        var opsPayload = JsonNode.Parse(await opsResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("demo", opsPayload["tenant"]!.GetValue<string>());

        var saasPayload = await ExecuteGraphQlAsync(client, """
            query SaaSOverview {
              saasArchitectureOverview(tenantSlug: "demo") {
                tenantSlug
                subscription {
                  plan
                  status
                }
                workspaces {
                  slug
                  memberCount
                  connectorCount
                  onboardingCompletedSteps
                  onboardingTotalSteps
                }
                apiClients {
                  clientId
                  scopes
                }
                usage {
                  metric
                  quantity
                }
              }
            }
            """);

        var overview = saasPayload["data"]?["saasArchitectureOverview"];
        Assert.NotNull(overview);
        Assert.Equal("demo", overview!["tenantSlug"]!.GetValue<string>());
        Assert.Equal("Pro", overview["subscription"]!["plan"]!.GetValue<string>());
        Assert.True(overview["workspaces"]!.AsArray()[0]!["connectorCount"]!.GetValue<int>() > 0);
        Assert.Contains(overview["apiClients"]!.AsArray(), clientNode =>
            clientNode?["clientId"]?.GetValue<string>() == "svc-demo-admin");
    }

    [Fact]
    public async Task TenantAdmin_CanRegisterConnector_AndCheckHealth()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var registerResponse = await client.PostAsJsonAsync("/graphql", new
        {
            operationName = "RegisterConnector",
            query = """
                mutation RegisterConnector($input: RegisterConnectorInput!) {
                  registerConnector(input: $input) {
                    dataSourceId
                    connectorType
                    status
                    sanitizedConfigurationJson
                  }
                }
                """,
            variables = new
            {
                input = new
                {
                    tenantSlug = "demo",
                    name = "CRM Mock Connector",
                    description = "GraphQL registration test.",
                    kind = "CRM",
                    connectorType = "mock",
                    configurationJson = """{"records":[{"externalUserId":"123","observedAtUtc":"2026-05-11T12:00:00Z","payload":{"crm":{"preferredChannel":"email"}}}]}"""
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var registerPayload = JsonNode.Parse(await registerResponse.Content.ReadAsStringAsync())!.AsObject();
        var dataSourceId = registerPayload["data"]?["registerConnector"]?["dataSourceId"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(dataSourceId));
        Assert.Equal("mock", registerPayload["data"]?["registerConnector"]?["connectorType"]?.GetValue<string>());

        var healthResponse = await client.PostAsJsonAsync("/graphql", new
        {
            operationName = "CheckConnectorHealth",
            query = """
                mutation CheckConnectorHealth($input: CheckConnectorHealthInput!) {
                  checkConnectorHealth(input: $input) {
                    dataSourceId
                    connectorType
                    isHealthy
                    status
                    messages
                  }
                }
                """,
            variables = new
            {
                input = new
                {
                    tenantSlug = "demo",
                    dataSourceId
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        var healthPayload = JsonNode.Parse(await healthResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(healthPayload["data"]?["checkConnectorHealth"]?["isHealthy"]?.GetValue<bool>());
    }

    [Fact]
    public async Task ApiClientLifecycle_HashesKey_TracksLastUsed_AndRevokesAccess()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var createResponse = await client.PostAsJsonAsync("/api/auth/api-clients", new
        {
            tenantSlug = "demo",
            workspaceSlug = "default",
            displayName = "Lifecycle Test Client",
            scopes = new[] { "context:read", "context:write" }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createPayload = JsonNode.Parse(await createResponse.Content.ReadAsStringAsync())!.AsObject();
        var clientId = createPayload["clientId"]!.GetValue<string>();
        var apiKey = createPayload["apiKey"]!.GetValue<string>();
        Assert.StartsWith("scout_live_", apiKey, StringComparison.Ordinal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
            var storedClient = await dbContext.ApiClients.SingleAsync(x => x.ClientId == clientId);
            Assert.NotEqual(apiKey, storedClient.SecretHash);
            Assert.StartsWith("pbkdf2-sha256$", storedClient.SecretHash, StringComparison.Ordinal);
        }

        client.DefaultRequestHeaders.Authorization = null;
        var tokenResponse = await client.PostAsJsonAsync("/api/auth/token", new
        {
            grantType = "client_credentials",
            clientId,
            clientSecret = apiKey,
            scope = "context:read"
        });

        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
            var storedClient = await dbContext.ApiClients.SingleAsync(x => x.ClientId == clientId);
            Assert.NotNull(storedClient.LastUsedAtUtc);
            Assert.Contains(await dbContext.AuditEvents.ToListAsync(), audit => audit.Action == "auth.token.issued" && audit.EntityId == storedClient.Id.ToString("D"));
        }

        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");
        var listResponse = await client.GetAsync("/api/auth/api-clients?tenantSlug=demo");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = JsonNode.Parse(await listResponse.Content.ReadAsStringAsync())!.AsArray();
        Assert.Contains(listPayload, item => item?["clientId"]?.GetValue<string>() == clientId && item?["apiKey"] is null);

        var revokeResponse = await client.PostAsJsonAsync($"/api/auth/api-clients/{clientId}/revoke", new
        {
            tenantSlug = "demo"
        });
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var revokedTokenResponse = await client.PostAsJsonAsync("/api/auth/token", new
        {
            grantType = "client_credentials",
            clientId,
            clientSecret = apiKey,
            scope = "context:read"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, revokedTokenResponse.StatusCode);
    }

    [Fact]
    public async Task GraphQl_ApiClientScopes_DenyWriteMutation_WhenTokenOnlyHasReadScope()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var createResponse = await client.PostAsJsonAsync("/api/auth/api-clients", new
        {
            tenantSlug = "demo",
            workspaceSlug = "default",
            displayName = "GraphQL read-only client",
            scopes = new[] { "context:read" }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createPayload = JsonNode.Parse(await createResponse.Content.ReadAsStringAsync())!.AsObject();
        var clientId = createPayload["clientId"]!.GetValue<string>();
        var apiKey = createPayload["apiKey"]!.GetValue<string>();

        client.DefaultRequestHeaders.Authorization = null;
        var tokenResponse = await client.PostAsJsonAsync("/api/auth/token", new
        {
            grantType = "client_credentials",
            clientId,
            clientSecret = apiKey,
            scope = "context:read"
        });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var tokenPayload = JsonNode.Parse(await tokenResponse.Content.ReadAsStringAsync())!.AsObject();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPayload["accessToken"]!.GetValue<string>());

        var readResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query ReadContext {
                  connectorCatalogue {
                    connectorType
                  }
                }
                """
        });
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var readPayload = JsonNode.Parse(await readResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Contains(readPayload["data"]?["connectorCatalogue"]?.AsArray() ?? [], item =>
            item?["connectorType"]?.GetValue<string>() == "csvUpload");

        var writeResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                mutation QueueContext {
                  queueContextRecompute(input: { tenantSlug: "demo", externalUserId: "user-123", triggeredBy: "scope-test" }) {
                    correlationId
                  }
                }
                """
        });
        Assert.Equal(HttpStatusCode.OK, writeResponse.StatusCode);
        var writePayload = JsonNode.Parse(await writeResponse.Content.ReadAsStringAsync())!.AsObject();
        var error = writePayload["errors"]!.AsArray().Single()!.AsObject();
        Assert.Equal("AUTHORIZATION_DENIED", error["extensions"]?["code"]?.GetValue<string>());
        Assert.Equal(403, error["extensions"]?["httpStatus"]?.GetValue<int>());
    }

    [Fact]
    public async Task TenantScopedUser_CannotReadAnotherTenant_AndDenialIsAudited()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query CrossTenantAttempt {
                  userProfiles(tenantSlug: "summit") {
                    externalUserId
                  }
                }
                """
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.NotNull(payload["errors"]);
        Assert.Null(payload["data"]?["userProfiles"]);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        Assert.Contains(await dbContext.AuditEvents.ToListAsync(), audit =>
            audit.Action == "auth.permission.denied"
            && audit.MetadataJson.Contains("cross-tenant-access", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdminConsole_GraphQlAndRest_DoNotLeakOtherTenants()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var ownTenantResponse = await client.GetAsync("/api/v1/admin/organisation?tenantSlug=demo");
        Assert.Equal(HttpStatusCode.OK, ownTenantResponse.StatusCode);
        var ownTenant = JsonNode.Parse(await ownTenantResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("demo", ownTenant["tenantSlug"]!.GetValue<string>());

        var otherTenantResponse = await client.GetAsync("/api/v1/admin/organisation?tenantSlug=summit");
        Assert.NotEqual(HttpStatusCode.OK, otherTenantResponse.StatusCode);
        var otherTenantBody = await otherTenantResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Summit", otherTenantBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin@summit.scout.local", otherTenantBody, StringComparison.OrdinalIgnoreCase);

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query CrossTenantAdminAttempt {
                  operatorAccounts(tenantSlug: "summit") {
                    email
                    role
                  }
                }
                """
        });

        Assert.Equal(HttpStatusCode.OK, graphQlResponse.StatusCode);
        var graphQlPayload = JsonNode.Parse(await graphQlResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.NotNull(graphQlPayload["errors"]);
        Assert.Null(graphQlPayload["data"]?["operatorAccounts"]);
        Assert.DoesNotContain("admin@summit.scout.local", graphQlPayload.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        Assert.Contains(await dbContext.AuditEvents.ToListAsync(), audit =>
            audit.Action == "auth.permission.denied"
            && audit.MetadataJson.Contains("cross-tenant-access", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TenantAdmin_CannotGrantPlatformOwner_FromAdminConsole()
    {
        await using var factory = new ScoutWebApplicationFactory();
        using var client = factory.CreateClient();
        AuthenticateAs(client, "tenant_admin", "admin@scout.local", "Dana Mercer");

        var usersResponse = await client.GetAsync("/api/v1/admin/users?tenantSlug=demo&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode);
        var usersPayload = JsonNode.Parse(await usersResponse.Content.ReadAsStringAsync())!.AsObject();
        var integrationAdmin = usersPayload["items"]!
            .AsArray()
            .Single(item => item?["email"]?.GetValue<string>() == "integrations@scout.local")!
            .AsObject();
        var userId = integrationAdmin["id"]!.GetValue<Guid>();

        var promoteResponse = await client.PatchAsJsonAsync($"/api/v1/admin/users/{userId}?tenantSlug=demo", new
        {
            displayName = "Riley Chen",
            role = "PlatformOwner",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Forbidden, promoteResponse.StatusCode);
        var promoteBody = await promoteResponse.Content.ReadAsStringAsync();
        Assert.Contains("Only platform owners can grant", promoteBody, StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        var account = await dbContext.OperatorAccounts.SingleAsync(x => x.Id == userId);
        Assert.Equal(KynticAI.Scout.Domain.Enums.OperatorRole.IntegrationAdmin, account.Role);
        Assert.Contains(await dbContext.AuditEvents.ToListAsync(), audit =>
            audit.Action == "auth.permission.denied"
            && audit.MetadataJson.Contains("platform-owner-escalation", StringComparison.Ordinal));
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

        var payload = JsonNode.Parse(content)!.AsObject();
        if (payload["errors"] is JsonArray errors)
        {
            var messages = errors
                .Select(error => error?["message"]?.GetValue<string>())
                .Where(message => !string.IsNullOrWhiteSpace(message));
            throw new InvalidOperationException("GraphQL errors: " + string.Join(" | ", messages));
        }

        return payload;
    }

    private sealed class ScoutWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"scout-tests-{Guid.NewGuid():N}";

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
