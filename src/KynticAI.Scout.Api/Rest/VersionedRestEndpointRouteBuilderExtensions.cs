using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Api.Auth;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Infrastructure.Auth;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;

namespace KynticAI.Scout.Api.Rest;

public static class VersionedRestEndpointRouteBuilderExtensions
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapScoutV1RestApi(this IEndpointRouteBuilder endpoints)
    {
        var v1 = endpoints.MapGroup("/api/v1")
            .WithTags("REST API v1");
        v1.AddEndpointFilter(async (context, next) =>
        {
            EndpointHttpContext.Set(context.HttpContext);
            try
            {
                return await next(context);
            }
            finally
            {
                EndpointHttpContext.Clear();
            }
        });

        v1.MapGet("/health", () => Results.Ok(new
            {
                status = "ok",
                version = "v1",
                service = "KynticAI.Scout.Api"
            }))
            .AllowAnonymous()
            .WithName("V1Health");

        v1.MapGet("/connectors/catalogue", async (
                string? availability,
                string? category,
                string? q,
                int? page,
                int? pageSize,
                IScoutService service,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var entries = await service.GetConnectorCatalogueAsync(cancellationToken);
                var filtered = entries.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(availability))
                {
                    filtered = filtered.Where(x => string.Equals(x.Availability, availability.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    filtered = filtered.Where(x => string.Equals(x.Category, category.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var term = q.Trim();
                    filtered = filtered.Where(x =>
                        x.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || x.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || x.ConnectorType.Contains(term, StringComparison.OrdinalIgnoreCase));
                }

                return Results.Ok(Page(filtered.ToList(), page, pageSize));
            }))
            .AllowAnonymous()
            .WithName("V1ListConnectorCatalogue");

        var reader = v1.MapGroup(string.Empty)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.Analyst, RoleNames.SalesUser, RoleNames.ReadOnly, RoleNames.ApiClient)
            })
            .RequireApiClientScope(ApiScopes.ContextRead);
        var writer = v1.MapGroup(string.Empty)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.Analyst, RoleNames.ApiClient)
            })
            .RequireApiClientScope(ApiScopes.ContextWrite);
        var selectorWriter = v1.MapGroup(string.Empty)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.Analyst, RoleNames.ApiClient)
            })
            .RequireApiClientScope(ApiScopes.SelectorsWrite);
        var eventIngestor = v1.MapGroup(string.Empty)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.Analyst, RoleNames.ApiClient)
            })
            .RequireApiClientScope(ApiScopes.EventsIngest);
        var auditReader = v1.MapGroup(string.Empty)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.Analyst, RoleNames.ReadOnly, RoleNames.ApiClient)
            })
            .RequireApiClientScope(ApiScopes.AuditRead);
        var billingReader = v1.MapGroup(string.Empty)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.ApiClient)
            })
            .RequireApiClientScope(ApiScopes.BillingRead);
        var blueprintWriter = v1.MapGroup(string.Empty)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.ApiClient)
            })
            .RequireApiClientScope(ApiScopes.BlueprintsWrite);
        var admin = v1.MapGroup(string.Empty)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.ApiClient)
            })
            .RequireApiClientScope(ApiScopes.AdminManage);

        reader.MapGet("/workspaces", async (
                string? tenantSlug,
                string? status,
                int? page,
                int? pageSize,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async httpContext =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var workspaces = await service.GetWorkspacesAsync(resolvedTenantSlug, status, cancellationToken);
                return Results.Ok(Page(workspaces, page, pageSize));
            }))
            .WithName("V1ListWorkspaces");

        reader.MapGet("/licence/status", async (
                ILicenceService service,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
                Results.Ok(await service.GetStatusAsync(cancellationToken))))
            .WithName("V1GetLicenceStatus");

        reader.MapGet("/context/users/{externalUserId}", async (
                string externalUserId,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async httpContext =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var result = await service.GetUserContextAsync(new UserContextLookupInput(resolvedTenantSlug, externalUserId), cancellationToken);
                return result is null ? NotFound(httpContext, "context.user_not_found", "User context was not found.") : Results.Ok(result);
            }))
            .WithName("V1GetUserContext");

        reader.MapGet("/context/accounts/{externalAccountId}", async (
                string externalAccountId,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async httpContext =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var result = await service.GetAccountContextAsync(resolvedTenantSlug, externalAccountId, cancellationToken);
                return result is null ? NotFound(httpContext, "context.account_not_found", "Account context was not found.") : Results.Ok(result);
            }))
            .WithName("V1GetAccountContext");

        reader.MapGet("/context/users/{externalUserId}/facts", async (
                string externalUserId,
                string? tenantSlug,
                string? attributeKey,
                int? page,
                int? pageSize,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async httpContext =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var context = await service.GetUserContextAsync(new UserContextLookupInput(resolvedTenantSlug, externalUserId), cancellationToken);
                if (context is null)
                {
                    return NotFound(httpContext, "context.user_not_found", "User context was not found.");
                }

                var facts = context.Facts.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(attributeKey))
                {
                    facts = facts.Where(x => x.AttributeKey.Contains(attributeKey.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                return Results.Ok(Page(facts.ToList(), page, pageSize));
            }))
            .WithName("V1ListUserContextFacts");

        reader.MapGet("/context/accounts/{externalAccountId}/facts", async (
                string externalAccountId,
                string? tenantSlug,
                string? attributeKey,
                int? page,
                int? pageSize,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async httpContext =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var account = await service.GetAccountContextAsync(resolvedTenantSlug, externalAccountId, cancellationToken);
                if (account is null)
                {
                    return NotFound(httpContext, "context.account_not_found", "Account context was not found.");
                }

                var facts = new List<ContextFactResult>();
                foreach (var user in account.Users.Where(x => x.LatestSnapshotId.HasValue))
                {
                    var snapshot = await service.GetContextSnapshotAsync(resolvedTenantSlug, user.LatestSnapshotId!.Value, cancellationToken);
                    if (snapshot is not null)
                    {
                        facts.AddRange(snapshot.Facts);
                    }
                }

                var filtered = facts.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(attributeKey))
                {
                    filtered = filtered.Where(x => x.AttributeKey.Contains(attributeKey.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                return Results.Ok(Page(filtered.ToList(), page, pageSize));
            }))
            .WithName("V1ListAccountContextFacts");

        reader.MapGet("/context/snapshots/{snapshotId:guid}", async (
                Guid snapshotId,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async httpContext =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var result = await service.GetContextSnapshotAsync(resolvedTenantSlug, snapshotId, cancellationToken);
                return result is null ? NotFound(httpContext, "context.snapshot_not_found", "Context snapshot was not found.") : Results.Ok(result);
            }))
            .WithName("V1GetContextSnapshot");

        reader.MapPost("/context/users/{externalUserId}/ai-safe-context-package", async (
                string externalUserId,
                V1AiSafeContextPackageRequest request,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async httpContext =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var result = await service.GetSalesContextPackageAsync(
                    new SalesContextPackageInput(resolvedTenantSlug, externalUserId, request.Objective),
                    cancellationToken);
                return result is null ? NotFound(httpContext, "context.package_not_found", "Governed evidence package was not found.") : Results.Ok(result);
            }))
            .WithName("V1GetAiSafeContextPackage");

        reader.MapPost("/intelligence/next-action", async (
                V1NextActionRequest request,
                string? tenantSlug,
                INextActionIntelligenceService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async httpContext =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug ?? request.Tenant);
                var result = await service.GenerateNextActionAsync(
                    new NextActionInput(
                        resolvedTenantSlug,
                        request.SubjectType,
                        request.SubjectIdentifier,
                        request.Objective,
                        request.Purpose,
                        request.ActorRole),
                    cancellationToken);
                return result is null
                    ? NotFound(httpContext, "intelligence.subject_not_found", "The intelligence subject was not found.")
                    : Results.Ok(result);
            }))
            .WithName("V1GenerateNextAction");

        writer.MapPost("/context/recompute", async (
                V1RecomputeRequest request,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var triggeredBy = string.IsNullOrWhiteSpace(request.TriggeredBy)
                    ? actorService.GetCurrentActor().Email
                    : request.TriggeredBy.Trim();
                var result = await service.QueueContextRecomputeAsync(
                    new QueueContextRecomputeInput(resolvedTenantSlug, request.ExternalUserId, triggeredBy),
                    cancellationToken);
                return Results.Accepted(value: result);
            }))
            .WithName("V1QueueContextRecompute");

        selectorWriter.MapPost("/selectors/preview", async (
                V1SelectorPreviewRequest request,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var input = new PreviewSelectorInput(
                    resolvedTenantSlug,
                    request.ExternalUserId,
                    request.SelectorDefinitionId,
                    request.DraftSelector);
                return Results.Ok(await service.PreviewSelectorAsync(input, cancellationToken));
            }))
            .WithName("V1PreviewSelector");

        selectorWriter.MapPost("/selectors/validate", async (
                V1SelectorValidateRequest request,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var draft = request.DraftSelector with { TenantSlug = resolvedTenantSlug };
                var input = new ValidateSelectorInput(resolvedTenantSlug, draft, request.ExternalUserId);
                return Results.Ok(await service.ValidateSelectorAsync(input, cancellationToken));
            }))
            .WithName("V1ValidateSelector");

        reader.MapGet("/semantic-attributes", async (
                string? tenantSlug,
                string? q,
                string? dataType,
                int? page,
                int? pageSize,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var attributes = await service.GetSemanticAttributesAsync(resolvedTenantSlug, cancellationToken);
                var filtered = attributes.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(q))
                {
                    filtered = filtered.Where(x =>
                        x.Key.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
                        || x.DisplayName.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(dataType))
                {
                    filtered = filtered.Where(x => string.Equals(x.DataType.ToString(), dataType.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                return Results.Ok(Page(filtered.ToList(), page, pageSize));
            }))
            .WithName("V1ListSemanticAttributes");

        auditReader.MapGet("/audit-events", async (
                string? tenantSlug,
                string? action,
                string? entityType,
                DateTime? fromUtc,
                DateTime? toUtc,
                int? page,
                int? pageSize,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var auditEvents = await service.GetAuditEventsAsync(resolvedTenantSlug, cancellationToken);
                var filtered = auditEvents.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(action))
                {
                    filtered = filtered.Where(x => x.Action.Contains(action.Trim(), StringComparison.OrdinalIgnoreCase));
                }
                if (!string.IsNullOrWhiteSpace(entityType))
                {
                    filtered = filtered.Where(x => string.Equals(x.EntityType, entityType.Trim(), StringComparison.OrdinalIgnoreCase));
                }
                if (fromUtc.HasValue)
                {
                    filtered = filtered.Where(x => x.CreatedAtUtc >= fromUtc.Value);
                }
                if (toUtc.HasValue)
                {
                    filtered = filtered.Where(x => x.CreatedAtUtc <= toUtc.Value);
                }

                return Results.Ok(Page(filtered.ToList(), page, pageSize));
            }))
            .WithName("V1ListAuditEvents");

        admin.MapGet("/audit-events/export", async (
                string? tenantSlug,
                string? format,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var export = await service.ExportAuditEventsAsync(resolvedTenantSlug, format ?? "json", cancellationToken);
                return Results.File(
                    Encoding.UTF8.GetBytes(export.Content),
                    export.ContentType,
                    export.FileName);
            }))
            .WithName("V1ExportAuditEvents");

        admin.MapGet("/admin/organisation", async (
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                return Results.Ok(await service.GetOrganisationSettingsAsync(resolvedTenantSlug, cancellationToken));
            }))
            .WithName("V1GetOrganisationSettings");

        admin.MapGet("/admin/users", async (
                string? tenantSlug,
                int? page,
                int? pageSize,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var users = await service.GetOperatorAccountsAsync(resolvedTenantSlug, cancellationToken);
                return Results.Ok(Page(users, page, pageSize));
            }))
            .WithName("V1ListOperatorAccounts");

        admin.MapPatch("/admin/users/{id:guid}", async (
                Guid id,
                V1UpdateOperatorAccountRequest request,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug ?? request.TenantSlug);
                return Results.Ok(await service.UpdateOperatorAccountAsync(
                    new UpdateOperatorAccountInput(
                        resolvedTenantSlug,
                        id,
                        request.DisplayName,
                        request.Role,
                        request.IsActive),
                    cancellationToken));
            }))
            .WithName("V1UpdateOperatorAccount");

        admin.MapGet("/blueprints", async (
                string? tenantSlug,
                string? status,
                int? page,
                int? pageSize,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var imports = await service.GetBlueprintImportsAsync(resolvedTenantSlug, status, cancellationToken);
                return Results.Ok(Page(imports, page, pageSize));
            }))
            .WithName("V1ListBlueprintImports");

        admin.MapGet("/governance/policies", async (
                string? tenantSlug,
                int? page,
                int? pageSize,
                IScoutService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var policies = await service.GetGovernancePoliciesAsync(resolvedTenantSlug, cancellationToken);
                return Results.Ok(Page(policies, page, pageSize));
            }))
            .WithName("V1ListGovernancePolicies");

        billingReader.MapGet("/billing/usage", async (
                string? tenantSlug,
                IUsageMeteringService usageMeteringService,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                return Results.Ok(await usageMeteringService.GetUsageOverviewAsync(resolvedTenantSlug, cancellationToken));
            }))
            .WithName("V1GetBillingUsage");

        eventIngestor.MapPost("/events/source-system", async (
                HttpRequest httpRequest,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                WebhookSigningSecretService webhookSigningSecretService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(_ => IngestSourceSystemEventRequestAsync(
                httpRequest,
                tenantSlug,
                dataSourceId: null,
                service,
                actorService,
                webhookSigningSecretService,
                cancellationToken)))
            .WithName("V1IngestSourceSystemEvent");

        eventIngestor.MapPost("/connectors/{dataSourceId:guid}/events/source-system", async (
                Guid dataSourceId,
                HttpRequest httpRequest,
                string? tenantSlug,
                IScoutService service,
                ICurrentActorService actorService,
                WebhookSigningSecretService webhookSigningSecretService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(_ => IngestSourceSystemEventRequestAsync(
                httpRequest,
                tenantSlug,
                dataSourceId,
                service,
                actorService,
                webhookSigningSecretService,
                cancellationToken)))
            .WithName("V1IngestConnectorSourceSystemEvent");

        admin.MapPost("/api-clients", async (
                V1CreateApiClientRequest request,
                string? tenantSlug,
                ApiClientKeyService apiClientKeyService,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var result = await apiClientKeyService.CreateAsync(
                    resolvedTenantSlug,
                    request.WorkspaceSlug,
                    request.DisplayName,
                    request.Scopes,
                    cancellationToken);
                return Results.Created($"/api/v1/api-clients/{result.ClientId}", result);
            }))
            .WithName("V1CreateApiClient");

        admin.MapGet("/webhook-signing-secrets", async (
                string? tenantSlug,
                int? page,
                int? pageSize,
                WebhookSigningSecretService webhookSigningSecretService,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var secrets = await webhookSigningSecretService.ListAsync(resolvedTenantSlug, cancellationToken);
                return Results.Ok(Page(secrets, page, pageSize));
            }))
            .WithName("V1ListWebhookSigningSecrets");

        admin.MapPost("/webhook-signing-secrets", async (
                V1CreateWebhookSigningSecretRequest request,
                string? tenantSlug,
                WebhookSigningSecretService webhookSigningSecretService,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                var result = await webhookSigningSecretService.CreateAsync(resolvedTenantSlug, request.WorkspaceSlug, request.DisplayName, cancellationToken);
                return Results.Created($"/api/v1/webhook-signing-secrets/{result.SecretId}", result);
            }))
            .WithName("V1CreateWebhookSigningSecret");

        blueprintWriter.MapPost("/blueprints/upload", async (
                UploadBlueprintInput request,
                string? tenantSlug,
                IBlueprintImportService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug ?? request.TenantSlug);
                return Results.Created(
                    "/api/v1/blueprints",
                    await service.UploadAsync(request with { TenantSlug = resolvedTenantSlug }, cancellationToken));
            }))
            .WithName("V1UploadBlueprint");

        blueprintWriter.MapPost("/blueprints/validate", async (
                BlueprintImportInput request,
                string? tenantSlug,
                IBlueprintImportService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug ?? request.TenantSlug);
                return Results.Ok(await service.ValidateAsync(request with { TenantSlug = resolvedTenantSlug }, cancellationToken));
            }))
            .WithName("V1ValidateBlueprint");

        blueprintWriter.MapPost("/blueprints/preview", async (
                BlueprintImportInput request,
                string? tenantSlug,
                IBlueprintImportService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug ?? request.TenantSlug);
                return Results.Ok(await service.PreviewAsync(request with { TenantSlug = resolvedTenantSlug }, cancellationToken));
            }))
            .WithName("V1PreviewBlueprint");

        blueprintWriter.MapPost("/blueprints/import", async (
                BlueprintImportInput request,
                string? tenantSlug,
                IBlueprintImportService service,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug ?? request.TenantSlug);
                return Results.Ok(await service.ImportAsync(request with { TenantSlug = resolvedTenantSlug }, cancellationToken));
            }))
            .WithName("V1ImportBlueprint");

        admin.MapPost("/api-clients/{id}/rotate", async (
                string id,
                string? tenantSlug,
                ApiClientKeyService apiClientKeyService,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                return Results.Ok(await apiClientKeyService.RotateAsync(resolvedTenantSlug, id, cancellationToken));
            }))
            .WithName("V1RotateApiClient");

        admin.MapDelete("/api-clients/{id}", async (
                string id,
                string? tenantSlug,
                ApiClientKeyService apiClientKeyService,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                await apiClientKeyService.RevokeAsync(resolvedTenantSlug, id, cancellationToken);
                return Results.NoContent();
            }))
            .WithName("V1RevokeApiClient");

        admin.MapPost("/webhook-signing-secrets/{id}/rotate", async (
                string id,
                string? tenantSlug,
                WebhookSigningSecretService webhookSigningSecretService,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                return Results.Ok(await webhookSigningSecretService.RotateAsync(resolvedTenantSlug, id, cancellationToken));
            }))
            .WithName("V1RotateWebhookSigningSecret");

        admin.MapDelete("/webhook-signing-secrets/{id}", async (
                string id,
                string? tenantSlug,
                WebhookSigningSecretService webhookSigningSecretService,
                ICurrentActorService actorService,
                CancellationToken cancellationToken) =>
            await ExecuteAsync(async _ =>
            {
                var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
                await webhookSigningSecretService.RevokeAsync(resolvedTenantSlug, id, cancellationToken);
                return Results.NoContent();
            }))
            .WithName("V1RevokeWebhookSigningSecret");

        return endpoints;
    }

    private static RouteGroupBuilder RequireApiClientScope(this RouteGroupBuilder group, string requiredScope)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var user = context.HttpContext.User;
            if (!user.IsInRole(RoleNames.ApiClient))
            {
                return await next(context);
            }

            var scopes = user.FindAll("scope")
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(ApiScopes.Normalize)
                .ToHashSet(StringComparer.Ordinal);
            if (scopes.Contains(requiredScope))
            {
                return await next(context);
            }

            return Error(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                "authorization.scope_denied",
                $"API client scope '{requiredScope}' is required.");
        });

        return group;
    }

    private static string ResolveTenantSlug(ICurrentActorService actorService, string? requestedTenantSlug)
    {
        var actor = actorService.GetCurrentActor();
        if (string.IsNullOrWhiteSpace(requestedTenantSlug))
        {
            return actor.TenantSlug;
        }

        var normalizedRequestedTenantSlug = requestedTenantSlug.Trim().ToLowerInvariant();
        if (actor.IsSystem || actor.IsPlatformOwner)
        {
            return normalizedRequestedTenantSlug;
        }

        if (string.Equals(actor.TenantSlug, normalizedRequestedTenantSlug, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedRequestedTenantSlug;
        }

        throw new UnauthorizedAccessException("Cross-tenant access is not permitted.");
    }

    private static V1PagedResponse<T> Page<T>(IReadOnlyList<T> items, int? page, int? pageSize)
    {
        var resolvedPage = Math.Max(DefaultPage, page ?? DefaultPage);
        var resolvedPageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var skipped = (resolvedPage - 1) * resolvedPageSize;
        var pageItems = items.Skip(skipped).Take(resolvedPageSize).ToList();
        return new V1PagedResponse<T>(
            pageItems,
            resolvedPage,
            resolvedPageSize,
            items.Count,
            skipped + pageItems.Count < items.Count);
    }

    private static async Task<IResult> IngestSourceSystemEventRequestAsync(
        HttpRequest httpRequest,
        string? tenantSlug,
        Guid? dataSourceId,
        IScoutService service,
        ICurrentActorService actorService,
        WebhookSigningSecretService webhookSigningSecretService,
        CancellationToken cancellationToken)
    {
        var resolvedTenantSlug = ResolveTenantSlug(actorService, tenantSlug);
        var body = await ReadBodyAsync(httpRequest, cancellationToken);
        var request = JsonSerializer.Deserialize<V1SourceSystemEventRequest>(body, JsonOptions)
            ?? throw new InvalidOperationException("A source-system event body is required.");
        var eventId = request.EventId ?? httpRequest.Headers["X-Scout-Event-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        var signatureResult = await ValidateWebhookSignatureAsync(
            httpRequest.HttpContext,
            webhookSigningSecretService,
            resolvedTenantSlug,
            request.WorkspaceSlug,
            eventId,
            body,
            cancellationToken);
        if (!signatureResult.Accepted)
        {
            return Error(
                httpRequest.HttpContext,
                StatusCodes.Status401Unauthorized,
                "webhook.signature_invalid",
                $"Webhook signature validation failed: {signatureResult.Reason}.");
        }

        var payloadJson = request.PayloadJson
            ?? JsonSerializer.Serialize(request.Payload ?? new { }, JsonOptions);
        var result = await service.IngestSourceSystemEventAsync(
            new SourceSystemEventInput(
                resolvedTenantSlug,
                request.WorkspaceSlug,
                eventId,
                request.SourceSystem,
                request.EventType,
                payloadJson,
                request.ExternalUserId,
                request.ExternalAccountId,
                request.ObservedAtUtc,
                dataSourceId),
            cancellationToken);
        return Results.Accepted(value: result);
    }

    private static async Task<IResult> ExecuteAsync(Func<HttpContext, Task<IResult>> action)
    {
        try
        {
            return await action(EndpointHttpContext.Current);
        }
        catch (ValidationException exception)
        {
            return Error(
                EndpointHttpContext.Current,
                StatusCodes.Status400BadRequest,
                "validation.failed",
                "The request did not pass validation.",
                exception.Errors
                    .GroupBy(error => string.IsNullOrWhiteSpace(error.PropertyName) ? "request" : error.PropertyName)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Error(EndpointHttpContext.Current, StatusCodes.Status403Forbidden, "authorization.denied", exception.Message);
        }
        catch (PlanLimitExceededException exception)
        {
            return Error(
                EndpointHttpContext.Current,
                StatusCodes.Status402PaymentRequired,
                "billing.limit_exceeded",
                exception.Message,
                new Dictionary<string, string[]>
                {
                    ["tenantSlug"] = [exception.TenantSlug],
                    ["plan"] = [exception.Plan.ToString()],
                    ["metric"] = [exception.Metric.ToString()],
                    ["limit"] = [exception.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                    ["currentUsage"] = [exception.CurrentUsage.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                    ["requestedQuantity"] = [exception.RequestedQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)]
                });
        }
        catch (InvalidOperationException exception)
        {
            return Error(EndpointHttpContext.Current, StatusCodes.Status400BadRequest, "request.invalid", exception.Message);
        }
    }

    private static IResult NotFound(HttpContext httpContext, string code, string message)
        => Error(httpContext, StatusCodes.Status404NotFound, code, message);

    private static IResult Error(
        HttpContext httpContext,
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? details = null)
    {
        var correlationId = httpContext.Response.Headers["X-Request-Id"].FirstOrDefault()
            ?? httpContext.TraceIdentifier;
        return Results.Json(
            new V1ErrorResponse(new V1ErrorBody(code, message, correlationId, details)),
            statusCode: statusCode);
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task<WebhookSignatureValidationResult> ValidateWebhookSignatureAsync(
        HttpContext httpContext,
        WebhookSigningSecretService webhookSigningSecretService,
        string tenantSlug,
        string? workspaceSlug,
        string eventId,
        string body,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(httpContext.User.Identity?.AuthenticationType, ApiKeyAuthenticationHandler.SchemeName, StringComparison.Ordinal))
        {
            return new WebhookSignatureValidationResult(true, "non_api_key_actor");
        }

        var webhookSecretId = httpContext.Request.Headers["X-Scout-Webhook-Secret-Id"].FirstOrDefault();
        var webhookSecret = httpContext.Request.Headers["X-Scout-Webhook-Secret"].FirstOrDefault();
        var signature = httpContext.Request.Headers["X-Scout-Webhook-Signature"].FirstOrDefault();
        var timestamp = httpContext.Request.Headers["X-Scout-Webhook-Timestamp"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(webhookSecretId))
        {
            if (string.IsNullOrWhiteSpace(webhookSecret)
                || string.IsNullOrWhiteSpace(signature)
                || string.IsNullOrWhiteSpace(timestamp))
            {
                return new WebhookSignatureValidationResult(false, "missing_webhook_secret_headers");
            }

            return await webhookSigningSecretService.ValidateAsync(
                tenantSlug,
                workspaceSlug,
                webhookSecretId,
                webhookSecret,
                timestamp,
                eventId,
                body,
                signature,
                cancellationToken);
        }

        var rawApiKey = httpContext.Items[ApiKeyAuthenticationHandler.RawApiKeyItemName] as string;
        if (string.IsNullOrWhiteSpace(rawApiKey)
            || string.IsNullOrWhiteSpace(signature)
            || string.IsNullOrWhiteSpace(timestamp))
        {
            return new WebhookSignatureValidationResult(false, "missing_legacy_headers");
        }

        return WebhookSigningSecretService.VerifyLegacyApiKeyHmac(rawApiKey, timestamp, body, signature)
            ? new WebhookSignatureValidationResult(true, "legacy_api_key_hmac")
            : new WebhookSignatureValidationResult(false, "legacy_api_key_hmac_invalid");
    }

    private sealed class EndpointHttpContext
    {
        private static readonly AsyncLocal<HttpContext?> CurrentContext = new();

        public static HttpContext Current
            => CurrentContext.Value ?? throw new InvalidOperationException("No endpoint HttpContext is available.");

        public static void Set(HttpContext httpContext)
            => CurrentContext.Value = httpContext;

        public static void Clear()
            => CurrentContext.Value = null;
    }
}
