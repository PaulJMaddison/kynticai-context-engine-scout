using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Sdk;

namespace KynticAI.Scout.EndToEndTests;

/// <summary>
/// Verifies the .NET SDK (KynticAI.Scout.Sdk) can call the API hosted
/// in-memory: create resources, query resources, and handle errors.
///
/// <see cref="ContextFactResult.ValueType"/> round-trips as the typed
/// <see cref="FactValueType"/> enum against the API's integer wire encoding.
/// </summary>
public sealed class SdkIntegrationE2ETests : IAsyncLifetime
{
    private readonly ScoutWebApplicationFactory factory = new();
    private HttpClient httpClient = null!;
    private ScoutClient sdkClient = null!;
    private string accessToken = null!;

    public async Task InitializeAsync()
    {
        await factory.SeedGoldenPathDataAsync();
        httpClient = factory.CreateClient();

        ScoutWebApplicationFactory.RemoveAuthentication(httpClient);
        var tokenResponse = await httpClient.PostAsJsonAsync("/api/auth/token",
            new KynticAI.Scout.Api.Auth.MachineTokenRequest(
                "client_credentials",
                "e2e-machine-client",
                "e2e-machine-secret-value-for-tests",
                "context:read context:write selectors:write events:ingest audit:read admin:manage blueprints:write billing:read"));

        var tokenPayload = JsonNode.Parse(await tokenResponse.Content.ReadAsStringAsync())!.AsObject();
        accessToken = tokenPayload["accessToken"]!.GetValue<string>();

        sdkClient = new ScoutClient(httpClient, new ScoutClientOptions
        {
            BaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/'),
            GraphQlEndpoint = "/graphql",
            AccessToken = accessToken
        });
    }

    public async Task DisposeAsync()
    {
        sdkClient.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Sdk_GetUserContext_ReturnsProfile()
    {
        var context = await sdkClient.Users.GetContextAsync("e2e-tenant", "user-e2e-001");

        Assert.NotNull(context);
        Assert.Equal("user-e2e-001", context!.ExternalUserId);
        Assert.Equal("Jordan Rivera", context.FullName);
        Assert.True(context.OverallConfidence > 0, "Context should have positive confidence.");
        Assert.Equal(2, context.Facts.Count);
    }

    [Fact]
    public async Task Sdk_GetUserContext_ReturnsNullForMissingUser()
    {
        var exception = await Assert.ThrowsAsync<ScoutException>(() =>
            sdkClient.Users.GetContextAsync("e2e-tenant", "non-existent-user"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("context.user_not_found", exception.Code);
    }

    [Fact]
    public async Task Sdk_GetAccountContext_ReturnsAccount()
    {
        var context = await sdkClient.Accounts.GetContextAsync("e2e-tenant", "acct-e2e-001");

        Assert.NotNull(context);
        Assert.Equal("acct-e2e-001", context!.ExternalAccountId);
        Assert.Equal("Acme E2E Corp", context.AccountName);
        Assert.Single(context.Users);
    }

    [Fact]
    public async Task Sdk_GetSnapshotById_ReturnsSnapshot()
    {
        var snapshotId = ScoutWebApplicationFactory.SeedIds.SnapshotId;
        var snapshot = await sdkClient.Snapshots.GetByIdAsync("e2e-tenant", snapshotId);

        Assert.NotNull(snapshot);
        Assert.Equal(snapshotId, snapshot!.SnapshotId);
        Assert.Equal(2, snapshot.Facts.Count);
        Assert.True(snapshot.OverallConfidence > 0, "Snapshot should have positive confidence.");
    }

    [Fact]
    public async Task Sdk_GetUserFacts_ReturnsFacts()
    {
        var facts = await sdkClient.Facts.GetForUserAsync("e2e-tenant", "user-e2e-001");

        Assert.Equal(2, facts.Count);
        var conversionFact = Assert.Single(facts, f => f.AttributeKey == "conversionProbability");
        var churnFact = Assert.Single(facts, f => f.AttributeKey == "churnRisk");
        Assert.Equal(FactValueType.Number, conversionFact.ValueType);
        Assert.Equal(FactValueType.Enum, churnFact.ValueType);
    }

    [Fact]
    public async Task Sdk_GetUserFacts_SupportsAttributeKeyFilter()
    {
        var facts = await sdkClient.Facts.GetForUserAsync(
            "e2e-tenant",
            "user-e2e-001",
            new ContextFactLookupOptions(AttributeKey: "churnRisk"));

        var churnFact = Assert.Single(facts);
        Assert.Equal("churnRisk", churnFact.AttributeKey);
        Assert.Equal(FactValueType.Enum, churnFact.ValueType);
    }

    [Fact]
    public async Task Sdk_ForTenant_ScopesAllCalls()
    {
        var context = await sdkClient.Users.GetContextAsync("e2e-tenant", "user-e2e-001");
        Assert.NotNull(context);
        Assert.Equal("user-e2e-001", context!.ExternalUserId);

        var tenantClient = sdkClient.ForTenant("e2e-tenant");
        var accountContext = await tenantClient.Accounts.GetContextAsync("acct-e2e-001");
        Assert.NotNull(accountContext);
        Assert.Equal("acct-e2e-001", accountContext!.ExternalAccountId);
    }

    [Fact]
    public async Task Sdk_QueueRecompute_ReturnsCorrelationId()
    {
        var result = await sdkClient.Recompute.QueueForUserAsync("e2e-tenant", "user-e2e-001", "sdk-e2e-test");

        Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId), "Recompute result should have a correlation ID.");
        Assert.Equal(ScoutWebApplicationFactory.SeedIds.TenantId, result.TenantId);
    }

    [Fact]
    public async Task Sdk_GetAuditEvents_ReturnsEvents()
    {
        var events = await sdkClient.Audit.GetEventsAsync("e2e-tenant");

        Assert.NotNull(events);
    }

    [Fact]
    public async Task Sdk_IngestSourceSystemEvent_AcceptsEvent()
    {
        var result = await sdkClient.Events.IngestSourceSystemEventAsync("e2e-tenant",
            new SourceSystemEventRequest(
                $"evt-sdk-{Guid.NewGuid():N}",
                "primary",
                "warehouse",
                "account.updated",
                new { health = "green" },
                null,
                "user-e2e-001",
                "acct-e2e-001",
                DateTime.UtcNow));

        Assert.NotNull(result);
        Assert.Equal("e2e-tenant", result.TenantSlug);
        Assert.False(result.IsDuplicate, "First event should not be a duplicate.");
    }

    [Fact]
    public async Task Sdk_GetLatestSnapshotForUser_ReturnsSummary()
    {
        var summary = await sdkClient.Snapshots.GetLatestForUserAsync("e2e-tenant", "user-e2e-001");

        Assert.NotNull(summary);
        Assert.True(summary!.FactCount >= 2, "Snapshot summary should contain at least 2 facts.");
        Assert.True(summary.OverallConfidence > 0, "Snapshot summary should have positive confidence.");
    }
}
