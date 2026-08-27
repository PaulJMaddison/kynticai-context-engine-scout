using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Api.Rest;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
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
public sealed class V1RestApiIntegrationTests
{
    [Fact]
    public async Task V1RestApi_SupportsJwtApiKeyScopingAndContextEndpoints()
    {
        await using var factory = new V1RestWebApplicationFactory();
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var publicCatalogue = await ReadJsonAsync(client.GetAsync("/api/v1/connectors/catalogue?pageSize=100"));
        var catalogueItems = publicCatalogue.Payload["items"]!.AsArray();
        Assert.Contains(catalogueItems, item => item?["connectorType"]?.GetValue<string>() == "mockCrm");
        Assert.Contains(catalogueItems, item =>
            item?["connectorType"]?.GetValue<string>() == "postgresql"
            && item?["publicStatus"]?.GetValue<string>() == "PublicGenericExample"
            && item?["isIncludedInOpenCore"]?.GetValue<bool>() == true);
        Assert.Contains(catalogueItems, item =>
            item?["connectorType"]?.GetValue<string>() == "sqlServer"
            && item?["publicStatus"]?.GetValue<string>() == "PaidEnterpriseImplementation"
            && item?["isPlaceholder"]?.GetValue<bool>() == true);
        Assert.Contains(catalogueItems, item =>
            item?["connectorType"]?.GetValue<string>() == "legacy-dotnet-handlers"
            && item?["publicStatus"]?.GetValue<string>() == "CustomerSpecificConnector"
            && item?["isPlaceholder"]?.GetValue<bool>() == true);
        Assert.Contains(catalogueItems, item =>
            item?["connectorType"]?.GetValue<string>() == "salesforce"
            && item?["isPlaceholder"]?.GetValue<bool>() == true
            && item?["availability"]?.GetValue<string>() == "SaaSManaged");

        AuthenticateAsTenantAdmin(client);
        var graphQlCatalogueResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query ConnectorCatalogue {
                  connectorCatalogue {
                    connectorType
                    publicStatus
                    availability
                    isIncludedInOpenCore
                    requiresCommercialAgreement
                    isPlaceholder
                  }
                }
                """
        });
        Assert.Equal(HttpStatusCode.OK, graphQlCatalogueResponse.StatusCode);
        var graphQlCataloguePayload = JsonNode.Parse(await graphQlCatalogueResponse.Content.ReadAsStringAsync())!.AsObject();
        var graphQlCatalogue = graphQlCataloguePayload["data"]!["connectorCatalogue"]!.AsArray();
        Assert.Contains(graphQlCatalogue, item => item?["connectorType"]?.GetValue<string>() == "csvUpload");
        Assert.Contains(graphQlCatalogue, item =>
            item?["connectorType"]?.GetValue<string>() == "hubspot"
            && item?["publicStatus"]?.GetValue<string>() == "PaidEnterpriseImplementation"
            && item?["requiresCommercialAgreement"]?.GetValue<bool>() == true);

        var blueprintJson = CreateBlueprintJson();
        var uploadResponse = await client.PostAsJsonAsync("/api/v1/blueprints/upload", new UploadBlueprintInput(
            "demo",
            "primary",
            "Integration blueprint",
            blueprintJson));
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploadBlueprint = JsonNode.Parse(await uploadResponse.Content.ReadAsStringAsync())!.AsObject();
        var importId = uploadBlueprint["importId"]!.GetValue<Guid>();
        Assert.Equal("Uploaded", uploadBlueprint["status"]!.GetValue<string>());

        var previewBlueprint = await ReadJsonAsync(client.PostAsJsonAsync("/api/v1/blueprints/preview", new BlueprintImportInput(
            "demo",
            importId,
            null)));
        Assert.True(previewBlueprint.Payload["isValid"]!.GetValue<bool>());
        Assert.True(previewBlueprint.Payload["preview"]!.AsArray().Count >= 6);

        var importedBlueprint = await ReadJsonAsync(client.PostAsJsonAsync("/api/v1/blueprints/import", new BlueprintImportInput(
            "demo",
            importId,
            null)));
        Assert.Equal("Imported", importedBlueprint.Payload["status"]!.GetValue<string>());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
            Assert.True(await dbContext.BlueprintImports.AnyAsync(x => x.Id == importId && x.Status == BlueprintImportStatus.Imported));
            Assert.True(await dbContext.PiiRules.AnyAsync(x => x.TenantId == SeedIds.TenantId && x.Key == "emailMasking"));
            Assert.True(await dbContext.AuditPolicies.AnyAsync(x => x.TenantId == SeedIds.TenantId && x.Key == "blueprintAudit"));
            Assert.True(await dbContext.AuditEvents.AnyAsync(x => x.TenantId == SeedIds.TenantId && x.Action == "blueprint.imported"));
        }

        var createdClientResponse = await client.PostAsJsonAsync("/api/v1/api-clients", new V1CreateApiClientRequest(
            "Warehouse ingestion client",
            "primary",
            ["context:read", "context:write", "selectors:write", "events:ingest", "audit:read"]));
        Assert.Equal(HttpStatusCode.Created, createdClientResponse.StatusCode);
        var createdClient = JsonNode.Parse(await createdClientResponse.Content.ReadAsStringAsync())!.AsObject();
        var clientId = createdClient["clientId"]!.GetValue<string>();
        var apiKey = createdClient["apiKey"]!.GetValue<string>();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        client.DefaultRequestHeaders.Add("X-Request-Id", "v1-rest-test-request");

        var workspaces = await ReadJsonAsync(client.GetAsync("/api/v1/workspaces?page=1&pageSize=10"));
        Assert.Equal("v1-rest-test-request", workspaces.Response.Headers.GetValues("X-Request-Id").Single());
        Assert.Single(workspaces.Payload["items"]!.AsArray());

        var userContext = await ReadJsonAsync(client.GetAsync("/api/v1/context/users/user-123"));
        var snapshotId = userContext.Payload["snapshotId"]!.GetValue<Guid>();
        Assert.Equal("user-123", userContext.Payload["externalUserId"]!.GetValue<string>());

        var accountContext = await ReadJsonAsync(client.GetAsync("/api/v1/context/accounts/acct-123"));
        Assert.Equal("acct-123", accountContext.Payload["externalAccountId"]!.GetValue<string>());
        Assert.Single(accountContext.Payload["users"]!.AsArray());

        var userFacts = await ReadJsonAsync(client.GetAsync("/api/v1/context/users/user-123/facts?attributeKey=accountHealth&pageSize=5"));
        Assert.Single(userFacts.Payload["items"]!.AsArray());

        var graphQlFactsResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query FactLookup {
                  contextFacts(
                    tenantSlug: "demo"
                    externalUserId: "user-123"
                    externalAccountId: null
                    attributeKey: "accountHealth"
                    skip: 0
                    take: 5
                  ) {
                    attributeKey
                    confidence
                    provenanceJson
                  }
                }
                """
        });
        Assert.Equal(HttpStatusCode.OK, graphQlFactsResponse.StatusCode);
        var graphQlFactsPayload = JsonNode.Parse(await graphQlFactsResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Single(graphQlFactsPayload["data"]!["contextFacts"]!.AsArray());

        var accountFacts = await ReadJsonAsync(client.GetAsync("/api/v1/context/accounts/acct-123/facts?pageSize=5"));
        Assert.Single(accountFacts.Payload["items"]!.AsArray());

        var snapshot = await ReadJsonAsync(client.GetAsync($"/api/v1/context/snapshots/{snapshotId}"));
        Assert.Equal(snapshotId, snapshot.Payload["snapshotId"]!.GetValue<Guid>());
        Assert.Single(snapshot.Payload["facts"]!.AsArray());

        var contextPackage = await ReadJsonAsync(client.PostAsJsonAsync(
            "/api/v1/context/users/user-123/ai-safe-context-package",
            new V1AiSafeContextPackageRequest("Book a discovery call for enterprise rollout.")));
        Assert.Equal("user-123", contextPackage.Payload["externalUserId"]!.GetValue<string>());
        Assert.Equal("Book a discovery call for enterprise rollout.", contextPackage.Payload["salesObjective"]!.GetValue<string>());

        var nextActionResponse = await client.PostAsJsonAsync(
            "/api/v1/intelligence/next-action",
            new V1NextActionRequest("demo", "email", "avery@example.test", "sale", "customer_outreach", "sales_rep"));
        Assert.Equal(HttpStatusCode.NotImplemented, nextActionResponse.StatusCode);
        var nextActionError = JsonNode.Parse(await nextActionResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(
            "feature.external_consumer_required",
            nextActionError["error"]!["code"]!.GetValue<string>());

        var attributes = await ReadJsonAsync(client.GetAsync("/api/v1/semantic-attributes?q=accountHealth&pageSize=5"));
        Assert.Single(attributes.Payload["items"]!.AsArray());

        var preview = await ReadJsonAsync(client.PostAsJsonAsync("/api/v1/selectors/preview", new V1SelectorPreviewRequest(
            "user-123",
            SeedIds.SelectorId,
            null)));
        Assert.True(preview.Payload["isSuccess"]!.GetValue<bool>());

        var validate = await ReadJsonAsync(client.PostAsJsonAsync("/api/v1/selectors/validate", new V1SelectorValidateRequest(
            CreateDraftSelector(),
            "user-123")));
        Assert.True(validate.Payload["isValid"]!.GetValue<bool>());

        var eventResponse = await SignedPostAsJsonAsync(client, "/api/v1/events/source-system", apiKey, new V1SourceSystemEventRequest(
            "evt-v1-rest-001",
            "primary",
            "warehouse",
            "account.updated",
            new { health = "green" },
            null,
            "user-123",
            "acct-123",
            DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.Accepted, eventResponse.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
            var storedEvent = await dbContext.SourceSystemEvents.SingleAsync(x => x.EventId == "evt-v1-rest-001");
            Assert.Equal(SourceSystemEventStatus.Processed, storedEvent.Status);
            Assert.True(await dbContext.RecomputeJobs.AnyAsync(x => x.CorrelationId == storedEvent.CorrelationId));
            Assert.True(await dbContext.SelectorExecutions.AnyAsync(x => x.CorrelationId == storedEvent.CorrelationId));
        }

        var recomputeResponse = await client.PostAsJsonAsync("/api/v1/context/recompute", new V1RecomputeRequest("user-123", "integration-test"));
        Assert.Equal(HttpStatusCode.Accepted, recomputeResponse.StatusCode);

        AuthenticateAsTenantAdmin(client);
        var billingUsage = await ReadJsonAsync(client.GetAsync("/api/v1/billing/usage"));
        Assert.Equal("Pro", billingUsage.Payload["plan"]!.GetValue<string>());
        Assert.Contains(billingUsage.Payload["usage"]!.AsArray(), item =>
            item?["metric"]?.GetValue<string>() == "ContextLookups"
            && item?["quantity"]?.GetValue<long>() >= 3);
        Assert.Contains(billingUsage.Payload["limits"]!.AsArray(), item =>
            item?["metric"]?.GetValue<string>() == "ApiClients"
            && item?["limit"]?.GetValue<long>() == 5);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var auditEvents = await ReadJsonAsync(client.GetAsync("/api/v1/audit-events?action=source-system"));
        Assert.True(auditEvents.Payload["items"]!.AsArray().Count >= 1);

        var missingUser = await client.GetAsync("/api/v1/context/users/missing-user");
        Assert.Equal(HttpStatusCode.NotFound, missingUser.StatusCode);
        var error = JsonNode.Parse(await missingUser.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("context.user_not_found", error["error"]!["code"]!.GetValue<string>());
        Assert.Equal("v1-rest-test-request", error["error"]!["correlationId"]!.GetValue<string>());

        var rotatedResponse = await client.PostAsync($"/api/v1/api-clients/{clientId}/rotate", null);
        Assert.Equal(HttpStatusCode.Forbidden, rotatedResponse.StatusCode);

        AuthenticateAsTenantAdmin(client);
        var adminRotateResponse = await client.PostAsync($"/api/v1/api-clients/{clientId}/rotate", null);
        Assert.Equal(HttpStatusCode.OK, adminRotateResponse.StatusCode);
        var deleteResponse = await client.DeleteAsync($"/api/v1/api-clients/{clientId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task SourceSystemEvents_AreIdempotent_AndRejectBadSignatures_AndStayTenantScoped()
    {
        await using var factory = new V1RestWebApplicationFactory();
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        AuthenticateAsTenantAdmin(client);
        var createdClientResponse = await client.PostAsJsonAsync("/api/v1/api-clients", new V1CreateApiClientRequest(
            "Webhook client",
            "primary",
            ["events:ingest"]));
        var createdClient = JsonNode.Parse(await createdClientResponse.Content.ReadAsStringAsync())!.AsObject();
        var clientId = createdClient["clientId"]!.GetValue<string>();
        var apiKey = createdClient["apiKey"]!.GetValue<string>();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

        var payload = new V1SourceSystemEventRequest(
            "evt-idempotent-001",
            "primary",
            "warehouse",
            "product_usage.updated",
            new { activeDays30 = 24 },
            null,
            "user-123",
            "acct-123",
            DateTime.UtcNow);

        var crossTenant = await SignedPostAsJsonAsync(client, "/api/v1/events/source-system?tenantSlug=beta", apiKey, payload);
        var first = await SignedPostAsJsonAsync(client, "/api/v1/events/source-system", apiKey, payload);
        var second = await SignedPostAsJsonAsync(client, "/api/v1/events/source-system", apiKey, payload);
        var badSignature = await SignedPostAsJsonAsync(
            client,
            "/api/v1/events/source-system",
            "wrong-secret",
            payload with { EventId = "evt-bad-signature-001" });
        var deadLetter = await SignedPostAsJsonAsync(
            client,
            "/api/v1/events/source-system",
            apiKey,
            payload with { EventId = "evt-dead-letter-001", ExternalUserId = "missing-user", ExternalAccountId = null });

        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badSignature.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, deadLetter.StatusCode);

        var secondPayload = JsonNode.Parse(await second.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(secondPayload["isDuplicate"]!.GetValue<bool>());

        var conflictingDuplicate = await SignedPostAsJsonAsync(
            client,
            "/api/v1/events/source-system",
            apiKey,
            payload with
            {
                Payload = new { activeDays30 = 99 }
            });
        Assert.Equal(HttpStatusCode.Conflict, conflictingDuplicate.StatusCode);
        var conflictPayload = JsonNode.Parse(await conflictingDuplicate.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(
            "source_event.identity_conflict",
            conflictPayload["error"]!["code"]!.GetValue<string>());
        var deadLetterPayload = JsonNode.Parse(await deadLetter.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("DeadLettered", deadLetterPayload["status"]!.GetValue<string>());

        AuthenticateAsTenantAdmin(client);
        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query EventHistory {
                  sourceSystemEvents(
                    tenantSlug: "demo",
                    workspaceSlug: "primary",
                    sourceSystem: "warehouse",
                    eventType: "product_usage.updated",
                    status: "processed"
                  ) {
                    eventId
                    status
                    matchedSelectorCount
                  }
                }
                """
        });
        Assert.Equal(HttpStatusCode.OK, graphQlResponse.StatusCode);
        var graphQlPayload = JsonNode.Parse(await graphQlResponse.Content.ReadAsStringAsync())!.AsObject();
        var graphQlEvents = graphQlPayload["data"]!["sourceSystemEvents"]!.AsArray();
        Assert.Single(graphQlEvents);
        Assert.Equal("evt-idempotent-001", graphQlEvents[0]!["eventId"]!.GetValue<string>());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        var demoTenant = await dbContext.Tenants.SingleAsync(x => x.Slug == "demo");
        var betaTenant = await dbContext.Tenants.SingleAsync(x => x.Slug == "beta");
        Assert.Single(await dbContext.SourceSystemEvents.Where(x => x.TenantId == demoTenant.Id && x.EventId == "evt-idempotent-001").ToListAsync());
        Assert.False(await dbContext.SourceSystemEvents.AnyAsync(x => x.TenantId == betaTenant.Id && x.EventId == "evt-idempotent-001"));
        Assert.False(await dbContext.SourceSystemEvents.AnyAsync(x => x.EventId == "evt-bad-signature-001"));
        Assert.True(await dbContext.AuditEvents.AnyAsync(x => x.TenantId == demoTenant.Id && x.Action == "auth.permission.denied"));
        Assert.True(await dbContext.AuditEvents.AnyAsync(x => x.TenantId == demoTenant.Id && x.Action == "source-system.event.ignored"));
        Assert.True(await dbContext.AuditEvents.AnyAsync(x => x.TenantId == demoTenant.Id && x.Action == "source-system.event.failed"));
    }

    [Fact]
    public async Task ConnectorSourceSystemEvents_RouteThroughLocalApiIngestPath_AndBindRegisteredDataSource()
    {
        await using var factory = new V1RestWebApplicationFactory();
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        AuthenticateAsTenantAdmin(client);
        var createdClientResponse = await client.PostAsJsonAsync("/api/v1/api-clients", new V1CreateApiClientRequest(
            "Registered connector ingest client",
            "primary",
            ["events:ingest"]));
        var createdClient = JsonNode.Parse(await createdClientResponse.Content.ReadAsStringAsync())!.AsObject();
        var clientId = createdClient["clientId"]!.GetValue<string>();
        var apiKey = createdClient["apiKey"]!.GetValue<string>();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

        var response = await SignedPostAsJsonAsync(
            client,
            $"/api/v1/connectors/{SeedIds.DataSourceId}/events/source-system",
            apiKey,
            new V1SourceSystemEventRequest(
                "evt-connector-route-001",
                "primary",
                "connector-local-api-route",
                "source_record.upserted",
                new
                {
                    warehouse = new
                    {
                        health = "green"
                    }
                },
                null,
                "user-123",
                "acct-123",
                DateTime.UtcNow));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("Processed", payload["status"]!.GetValue<string>());
        Assert.Equal(1, payload["storedSignalCount"]!.GetValue<int>());
        Assert.True(payload["matchedSelectorCount"]!.GetValue<int>() >= 1);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        var storedEvent = await dbContext.SourceSystemEvents.SingleAsync(x => x.EventId == "evt-connector-route-001");
        Assert.Equal(SeedIds.DataSourceId, storedEvent.DataSourceId);
        Assert.Equal(SeedIds.UserProfileId, storedEvent.UserProfileId);
        Assert.Equal(SourceSystemEventStatus.Processed, storedEvent.Status);
        var connectorSignals = await dbContext.UserSignals
            .Where(x =>
                x.DataSourceId == SeedIds.DataSourceId
                && x.Key == "connector-local-api-route.source_record.upserted")
            .ToListAsync();
        Assert.Contains(connectorSignals, x => x.ValueJson.Contains("\"health\":\"green\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebhookSigningSecrets_ValidateRotateRevokeReplayAndAudit()
    {
        await using var factory = new V1RestWebApplicationFactory();
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        AuthenticateAsTenantAdmin(client);
        var createdClientResponse = await client.PostAsJsonAsync("/api/v1/api-clients", new V1CreateApiClientRequest(
            "Webhook event client",
            "primary",
            ["events:ingest"]));
        var createdClient = JsonNode.Parse(await createdClientResponse.Content.ReadAsStringAsync())!.AsObject();
        var clientId = createdClient["clientId"]!.GetValue<string>();
        var apiKey = createdClient["apiKey"]!.GetValue<string>();

        var createdSecretResponse = await client.PostAsJsonAsync("/api/v1/webhook-signing-secrets", new V1CreateWebhookSigningSecretRequest("Primary webhook secret", "primary"));
        Assert.Equal(HttpStatusCode.Created, createdSecretResponse.StatusCode);
        var createdSecret = JsonNode.Parse(await createdSecretResponse.Content.ReadAsStringAsync())!.AsObject();
        var secretId = createdSecret["secretId"]!.GetValue<string>();
        var secret = createdSecret["secret"]!.GetValue<string>();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var firstPayload = new V1SourceSystemEventRequest("evt-webhook-secret-001", "primary", "warehouse", "account.updated", new { health = "green" }, null, "user-123", "acct-123", DateTime.UtcNow);
        var first = await SignedWebhookSecretPostAsJsonAsync(client, "/api/v1/events/source-system", secretId, secret, firstPayload);
        var replay = await SignedWebhookSecretPostAsJsonAsync(client, "/api/v1/events/source-system", secretId, secret, firstPayload);
        var badSignature = await SignedWebhookSecretPostAsJsonAsync(client, "/api/v1/events/source-system", secretId, "wrong-secret", firstPayload with { EventId = "evt-webhook-secret-bad" });
        var expired = await SignedWebhookSecretPostAsJsonAsync(client, "/api/v1/events/source-system", secretId, secret, firstPayload with { EventId = "evt-webhook-secret-expired" }, DateTimeOffset.UtcNow.AddMinutes(-20));
        var crossWorkspace = await SignedWebhookSecretPostAsJsonAsync(client, "/api/v1/events/source-system", secretId, secret, firstPayload with { EventId = "evt-webhook-secret-cross", WorkspaceSlug = "other" });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badSignature.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, crossWorkspace.StatusCode);

        AuthenticateAsTenantAdmin(client);
        var rotateResponse = await client.PostAsync($"/api/v1/webhook-signing-secrets/{secretId}/rotate", null);
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        var rotated = JsonNode.Parse(await rotateResponse.Content.ReadAsStringAsync())!.AsObject();
        var rotatedSecret = rotated["secret"]!.GetValue<string>();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var oldSecretAfterRotation = await SignedWebhookSecretPostAsJsonAsync(client, "/api/v1/events/source-system", secretId, secret, firstPayload with { EventId = "evt-webhook-secret-old" });
        var rotatedSecretWorks = await SignedWebhookSecretPostAsJsonAsync(client, "/api/v1/events/source-system", secretId, rotatedSecret, firstPayload with { EventId = "evt-webhook-secret-rotated" });

        Assert.Equal(HttpStatusCode.Unauthorized, oldSecretAfterRotation.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, rotatedSecretWorks.StatusCode);

        AuthenticateAsTenantAdmin(client);
        var revokeResponse = await client.DeleteAsync($"/api/v1/webhook-signing-secrets/{secretId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var revoked = await SignedWebhookSecretPostAsJsonAsync(client, "/api/v1/events/source-system", secretId, rotatedSecret, firstPayload with { EventId = "evt-webhook-secret-revoked" });

        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        Assert.True(await dbContext.WebhookSigningSecrets.AnyAsync(x => x.SecretId == secretId && x.SecretHash != secret && x.Status == WebhookSigningSecretStatus.Revoked));
        Assert.True(await dbContext.AuditEvents.AnyAsync(x => x.Action == "webhook.signature.accepted"));
        Assert.True(await dbContext.AuditEvents.AnyAsync(x => x.Action == "webhook.signature.rejected"));
        Assert.True(await dbContext.AuditEvents.AnyAsync(x => x.Action == "webhook.secret.denied"));
    }

    [Fact]
    public async Task ApiClientScopes_DenyWriteEndpoint_WhenClientOnlyHasReadScope()
    {
        await using var factory = new V1RestWebApplicationFactory();
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        AuthenticateAsTenantAdmin(client);
        var createdClientResponse = await client.PostAsJsonAsync("/api/v1/api-clients", new V1CreateApiClientRequest(
            "Read-only client",
            "primary",
            ["context:read"]));
        Assert.Equal(HttpStatusCode.Created, createdClientResponse.StatusCode);
        var createdClient = JsonNode.Parse(await createdClientResponse.Content.ReadAsStringAsync())!.AsObject();
        var clientId = createdClient["clientId"]!.GetValue<string>();
        var apiKey = createdClient["apiKey"]!.GetValue<string>();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

        var readResponse = await client.GetAsync("/api/v1/context/users/user-123");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var writeResponse = await SignedPostAsJsonAsync(client, "/api/v1/events/source-system", apiKey, new V1SourceSystemEventRequest(
            "evt-read-only-denied-001",
            "primary",
            "warehouse",
            "account.updated",
            new { health = "green" },
            null,
            "user-123",
            "acct-123",
            DateTime.UtcNow));

        Assert.Equal(HttpStatusCode.Forbidden, writeResponse.StatusCode);
        var error = JsonNode.Parse(await writeResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("authorization.scope_denied", error["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task BillingLimits_AreEnforced_ForTenantScopedUsage()
    {
        await using var factory = new V1RestWebApplicationFactory();
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        AuthenticateAsTenantAdmin(client);
        var createdClientResponse = await client.PostAsJsonAsync("/api/v1/api-clients", new V1CreateApiClientRequest(
            "Limit test client",
            "primary",
            ["events:ingest"]));
        Assert.Equal(HttpStatusCode.Created, createdClientResponse.StatusCode);
        var createdClient = JsonNode.Parse(await createdClientResponse.Content.ReadAsStringAsync())!.AsObject();
        var clientId = createdClient["clientId"]!.GetValue<string>();
        var apiKey = createdClient["apiKey"]!.GetValue<string>();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
            var utcNow = DateTime.UtcNow;
            var windowStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            dbContext.BillingUsageRecords.Add(BillingUsageRecord.Create(
                SeedIds.TenantId,
                SeedIds.WorkspaceId,
                BillingUsageMetric.SourceEventIngested,
                25_000,
                windowStart,
                windowStart.AddMonths(1),
                "test",
                "{}",
                utcNow));
            await dbContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Client-Id", clientId);
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var response = await SignedPostAsJsonAsync(client, "/api/v1/events/source-system", apiKey, new V1SourceSystemEventRequest(
            "evt-limit-001",
            "primary",
            "warehouse",
            "account.updated",
            new { health = "green" },
            null,
            "user-123",
            "acct-123",
            DateTime.UtcNow));

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var error = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("billing.limit_exceeded", error["error"]!["code"]!.GetValue<string>());
        Assert.Equal("SourceEvents", error["error"]!["details"]!["metric"]!.AsArray()[0]!.GetValue<string>());
    }

    private static async Task<(HttpResponseMessage Response, JsonObject Payload)> ReadJsonAsync(Task<HttpResponseMessage> responseTask)
    {
        var response = await responseTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (response, JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject());
    }

    private static UpsertSelectorDefinitionInput CreateDraftSelector()
        => new(
            null,
            "demo",
            SeedIds.DataSourceId,
            SeedIds.AttributeId,
            "Draft health selector",
            "Validates health from warehouse payload.",
            SelectorMappingKind.DirectFieldMapping,
            """{"rule":{"valuePath":"warehouse.health"}}""",
            "Health is {{sourceValue}}.",
            """{"requiredPaths":["warehouse.health"]}""",
            0.9m,
            1_440,
            1,
            null);

    private static string CreateBlueprintJson()
        => """
            {
              "version": "1.0",
              "name": "Integration blueprint",
              "tenantSlug": "demo",
              "dataSources": [
                {
                  "name": "Blueprint CRM",
                  "description": "Blueprint-created mock CRM source.",
                  "kind": "CRM",
                  "connectionConfig": { "connectorType": "mockCrm", "records": [] }
                }
              ],
              "semanticAttributes": [
                {
                  "key": "blueprintHealth",
                  "displayName": "Blueprint Health",
                  "description": "Health imported from a generated blueprint.",
                  "dataType": "ENUM",
                  "exampleValueJson": "\"green\"",
                  "isSystem": true
                }
              ],
              "selectors": [
                {
                  "name": "Blueprint health selector",
                  "description": "Maps blueprint CRM health into semantic context.",
                  "dataSourceName": "Blueprint CRM",
                  "targetAttributeKey": "blueprintHealth",
                  "mappingKind": "DIRECT_FIELD_MAPPING",
                  "expression": { "rule": { "valuePath": "crm.health" } },
                  "explanationTemplate": "Blueprint health is {{sourceValue}}.",
                  "validationSchema": { "requiredPaths": ["crm.health"] },
                  "defaultConfidence": 0.9,
                  "freshnessWindowMinutes": 1440,
                  "priority": 5,
                  "scheduleIntervalMinutes": 60,
                  "publish": true
                }
              ],
              "promptTemplates": [
                {
                  "name": "Blueprint Prompt",
                  "description": "Prompt imported from blueprint.",
                  "systemPrompt": "Use governed context only.",
                  "developerPrompt": "Return structured JSON.",
                  "userPromptTemplate": "Summarize {{customer.name}}.",
                  "outputSchema": { "type": "object" },
                  "guardrails": ["Do not invent facts."]
                }
              ],
              "piiRules": [
                {
                  "key": "emailMasking",
                  "displayName": "Email Masking",
                  "description": "Mask personal emails for lower privilege roles.",
                  "rule": { "fields": ["email"], "masking": "email" }
                }
              ],
              "auditPolicies": [
                {
                  "key": "blueprintAudit",
                  "displayName": "Blueprint Audit",
                  "description": "Audit blueprint-generated objects.",
                  "policy": { "events": ["blueprint.imported"], "retentionDays": 365 }
                }
              ]
            }
            """;

    private static Task<HttpResponseMessage> SignedPostAsJsonAsync(
        HttpClient client,
        string url,
        string apiKey,
        object payload)
    {
        var body = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(apiKey));
        var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Scout-Webhook-Timestamp", timestamp);
        request.Headers.Add("X-Scout-Webhook-Signature", signature);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SignedWebhookSecretPostAsJsonAsync(
        HttpClient client,
        string url,
        string secretId,
        string secret,
        V1SourceSystemEventRequest payload,
        DateTimeOffset? timestamp = null)
    {
        var body = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var resolvedTimestamp = (timestamp ?? DateTimeOffset.UtcNow).ToString("O");
        var eventId = payload.EventId ?? Guid.NewGuid().ToString("N");
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{resolvedTimestamp}.{eventId}.{body}"))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Scout-Webhook-Secret-Id", secretId);
        request.Headers.Add("X-Scout-Webhook-Secret", secret);
        request.Headers.Add("X-Scout-Webhook-Timestamp", resolvedTimestamp);
        request.Headers.Add("X-Scout-Webhook-Signature", signature);
        return client.SendAsync(request);
    }

    private static void AuthenticateAsTenantAdmin(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-API-Client-Id");
        client.DefaultRequestHeaders.Remove("X-API-Key");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("scout-tests-signing-key-1234567890"));
        var token = new JwtSecurityToken(
            issuer: "KynticAI.Scout.Tests",
            audience: "KynticAI.Scout.Tests",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, SeedIds.AdminId.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, SeedIds.AdminId.ToString("D")),
                new Claim("tenant_id", SeedIds.TenantId.ToString("D")),
                new Claim("tenant_slug", "demo"),
                new Claim("workspace_id", SeedIds.WorkspaceId.ToString("D")),
                new Claim("workspace_slug", "primary"),
                new Claim("display_name", "Demo Admin"),
                new Claim(ClaimTypes.Email, "admin@example.test"),
                new Claim(ClaimTypes.Role, "tenant_admin")
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var contextDbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        var customerOpsDbContext = scope.ServiceProvider.GetRequiredService<CustomerOpsDbContext>();
        var utcNow = DateTime.UtcNow;

        var tenant = Tenant.Create("demo", "Demo Tenant", utcNow);
        SetId(tenant, SeedIds.TenantId);
        var betaTenant = Tenant.Create("beta", "Beta Tenant", utcNow);
        var workspace = Workspace.Create(tenant.Id, "primary", "Primary", "Primary workspace", true, utcNow);
        SetId(workspace, SeedIds.WorkspaceId);
        var betaWorkspace = Workspace.Create(betaTenant.Id, "primary", "Primary", "Primary beta workspace", true, utcNow);
        var admin = OperatorAccount.Create(tenant.Id, "admin@example.test", "Demo Admin", "not-used", OperatorRole.TenantAdmin, utcNow);
        SetId(admin, SeedIds.AdminId);
        var member = WorkspaceMember.Create(tenant.Id, workspace.Id, admin.Id, WorkspaceMemberRole.Owner, utcNow);
        var user = UserProfile.Create(tenant.Id, "user-123", "Avery Stone", "avery@example.test", "Acme Corp", "VP Revenue", "enterprise", utcNow, utcNow);
        SetId(user, SeedIds.UserProfileId);
        var dataSource = DataSource.Create(tenant.Id, "warehouse", "Warehouse source", DataSourceKind.SqlMetric, """{"connectorType":"mockSignal"}""", utcNow);
        SetId(dataSource, SeedIds.DataSourceId);
        var attribute = SemanticAttributeDefinition.Create(tenant.Id, "accountHealth", "Account Health", "Account health signal.", SemanticDataType.Enum, "\"green\"", false, utcNow);
        SetId(attribute, SeedIds.AttributeId);
        var selector = SelectorDefinition.Create(tenant.Id, dataSource.Id, attribute.Id, "Health selector", "Maps warehouse health.", SelectorMappingKind.DirectFieldMapping, """{"rule":{"valuePath":"warehouse.health"}}""", "Health is {{sourceValue}}.", """{"requiredPaths":["warehouse.health"]}""", 0.95m, 1_440, 1, null, utcNow);
        SetId(selector, SeedIds.SelectorId);
        selector.Publish(utcNow);
        var snapshot = ContextSnapshot.Create(tenant.Id, user.Id, 1, "Acme Corp is healthy and ready for AI-assisted workflows.", 0.94m, utcNow);
        SetId(snapshot, SeedIds.SnapshotId);
        var fact = ContextFact.Create(tenant.Id, snapshot.Id, attribute.Id, selector.Id, "accountHealth", "\"green\"", FactValueType.Enum, 0.95m, utcNow, utcNow.AddDays(1), "Mapped from warehouse.", "[]", utcNow);
        var signal = UserSignal.Create(tenant.Id, user.Id, dataSource.Id, "warehouse.health", JsonSerializer.Serialize("green"), FactValueType.Enum, utcNow, "[]", utcNow);

        contextDbContext.Tenants.Add(tenant);
        contextDbContext.Tenants.Add(betaTenant);
        contextDbContext.Workspaces.Add(workspace);
        contextDbContext.Workspaces.Add(betaWorkspace);
        contextDbContext.OperatorAccounts.Add(admin);
        contextDbContext.WorkspaceMembers.Add(member);
        contextDbContext.TenantSubscriptions.Add(TenantSubscription.Create(
            tenant.Id,
            SubscriptionPlan.Pro,
            SubscriptionStatus.Active,
            "test-provider-demo",
            "{}",
            utcNow.AddDays(-7),
            null,
            utcNow.AddMonths(1),
            utcNow));
        contextDbContext.UserProfiles.Add(user);
        contextDbContext.DataSources.Add(dataSource);
        contextDbContext.SemanticAttributeDefinitions.Add(attribute);
        contextDbContext.SelectorDefinitions.Add(selector);
        contextDbContext.ContextSnapshots.Add(snapshot);
        contextDbContext.ContextFacts.Add(fact);
        contextDbContext.UserSignals.Add(signal);

        var opsTenant = CustomerOpsTenant.Create("demo", "Demo Tenant", utcNow);
        var account = CustomerAccount.Create(opsTenant.Id, "acct-123", "Acme Corp", "acme.example", "Logistics", "enterprise", "EMEA", "customer", "Dana", 500, 1_000_000m, utcNow);
        var contact = CustomerContact.Create(opsTenant.Id, account.Id, "contact-123", "user-123", "Avery Stone", "avery@example.test", "VP Revenue", "executive", "Revenue", "email", true, utcNow);
        var wonAccount = CustomerAccount.Create(opsTenant.Id, "acct-won", "Larkspur Logistics", "larkspur.example", "Logistics", "enterprise", "EMEA", "customer", "Dana", 480, 2_500_000m, utcNow);
        var wonContact = CustomerContact.Create(opsTenant.Id, wonAccount.Id, "contact-won", "user-won", "Morgan Stone", "morgan@larkspur.example", "VP Revenue", "executive", "Revenue", "email", true, utcNow);
        customerOpsDbContext.CustomerOpsTenants.Add(opsTenant);
        customerOpsDbContext.CustomerAccounts.Add(account);
        customerOpsDbContext.CustomerAccounts.Add(wonAccount);
        customerOpsDbContext.CustomerContacts.Add(contact);
        customerOpsDbContext.CustomerContacts.Add(wonContact);
        customerOpsDbContext.SalesOpportunities.Add(SalesOpportunity.Create(opsTenant.Id, account.Id, contact.Id, "opp-123", "Acme enterprise expansion", "proposal", 75_000m, 78, utcNow.AddDays(14), "expansion", true, utcNow));
        customerOpsDbContext.SalesOpportunities.Add(SalesOpportunity.Create(opsTenant.Id, wonAccount.Id, wonContact.Id, "opp-won", "Larkspur enterprise outcome", "closed_won", 70_000m, 100, utcNow.AddDays(-30), "new-business", false, utcNow));
        customerOpsDbContext.SalesActivities.Add(SalesActivity.Create(opsTenant.Id, account.Id, contact.Id, "meeting", "outbound", "positive_reply", "Champion asked for implementation pricing.", utcNow.AddDays(-2), utcNow));
        customerOpsDbContext.EmailEngagementEvents.Add(EmailEngagementEvent.Create(opsTenant.Id, contact.Id, "Enterprise Expansion", "reply", "email", "{}", utcNow.AddDays(-1), utcNow));
        customerOpsDbContext.WebConversionEvents.Add(WebConversionEvent.Create(opsTenant.Id, account.Id, contact.Id, "pricing_viewed", "pricing", "enterprise-demand", "email", 81m, utcNow.AddDays(-1), utcNow));
        customerOpsDbContext.ProductUsageSummaries.Add(ProductUsageSummary.Create(opsTenant.Id, account.Id, contact.Id, utcNow.Date, 24, 18, 42, 6, 64, 44, 50, 86, utcNow));
        customerOpsDbContext.ProductUsageSummaries.Add(ProductUsageSummary.Create(opsTenant.Id, wonAccount.Id, wonContact.Id, utcNow.Date, 23, 17, 39, 5, 59, 41, 50, 84, utcNow));
        customerOpsDbContext.BillingMetrics.Add(BillingMetric.Create(opsTenant.Id, account.Id, utcNow.Date, 7_200m, 86_400m, 0, 0, 6, "healthy", utcNow));
        customerOpsDbContext.BillingMetrics.Add(BillingMetric.Create(opsTenant.Id, wonAccount.Id, utcNow.Date, 7_000m, 84_000m, 0, 0, 5, "healthy", utcNow));

        await contextDbContext.SaveChangesAsync();
        await customerOpsDbContext.SaveChangesAsync();
    }

    private static void SetId<T>(T entity, Guid id)
        where T : class
        => typeof(T).BaseType!.GetProperty("Id")!.SetValue(entity, id);

    private static class SeedIds
    {
        public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid WorkspaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly Guid AdminId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid UserProfileId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        public static readonly Guid DataSourceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public static readonly Guid AttributeId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        public static readonly Guid SelectorId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        public static readonly Guid SnapshotId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    }

    private sealed class V1RestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot databaseRoot = new();
        private readonly string databaseName = $"v1-rest-tests-{Guid.NewGuid():N}";

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
                    ["ReferenceData:CustomerOpsEnabled"] = "true",
                    ["Auth:Issuer"] = "KynticAI.Scout.Tests",
                    ["Auth:Audience"] = "KynticAI.Scout.Tests",
                    ["Auth:SigningKey"] = "scout-tests-signing-key-1234567890",
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

                TestSeedHelper.UseFastPasswordHashing(services);
            });
        }
    }
}
