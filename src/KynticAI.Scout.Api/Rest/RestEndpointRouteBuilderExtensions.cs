using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Infrastructure.Auth;
using FluentValidation;

namespace KynticAI.Scout.Api.Rest;

public static class RestEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapScoutRestApi(this IEndpointRouteBuilder endpoints)
    {
        var restGroup = endpoints.MapGroup("/api/rest")
            .RequireAuthorization();
        restGroup.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers["Deprecation"] = "true";
            context.HttpContext.Response.Headers["Link"] = "</api/v1>; rel=successor-version";
            // The legacy REST surface predates machine-client scopes. Keep it available to human
            // operators for compatibility, but require machine clients to use the scoped /api/v1
            // contract instead of silently bypassing their declared scopes here.
            if (context.HttpContext.User.FindFirst("client_id") is not null)
            {
                return Results.Problem(
                    title: "Legacy API is not available to machine clients",
                    detail: "Use the scoped /api/v1 endpoints for machine-to-machine access.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });

        var tenantAdminGroup = restGroup.MapGroup(string.Empty)
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin));

        var readerGroup = restGroup.MapGroup(string.Empty)
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin, RoleNames.Analyst, RoleNames.SalesUser, RoleNames.ReadOnly, RoleNames.ApiClient));

        readerGroup.MapGet("/tenants/{tenantSlug}/users/{externalUserId}/context",
            async (string tenantSlug, string externalUserId, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteAsync(() => service.GetUserContextAsync(new UserContextLookupInput(tenantSlug, externalUserId), cancellationToken)))
            .WithName("LegacyGetUserContext");

        readerGroup.MapGet("/tenants/{tenantSlug}/users/{externalUserId}/facts",
            async (string tenantSlug, string externalUserId, IScoutService service, CancellationToken cancellationToken) =>
            {
                var context = await service.GetUserContextAsync(new UserContextLookupInput(tenantSlug, externalUserId), cancellationToken);
                return context is null
                    ? Results.NotFound()
                    : Results.Ok(context.Facts);
            })
            .WithName("LegacyListUserContextFacts");

        readerGroup.MapPost("/tenants/{tenantSlug}/users/{externalUserId}/sales-context-package",
            async (string tenantSlug, string externalUserId, SalesContextPackageRequest request, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteAsync(() => service.GetSalesContextPackageAsync(
                    new SalesContextPackageInput(tenantSlug, externalUserId, request.SalesObjective),
                    cancellationToken)))
            .WithName("LegacyGetSalesContextPackage");

        readerGroup.MapGet("/tenants/{tenantSlug}/audit-events",
            async (string tenantSlug, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteAsync(async () => await service.GetAuditEventsAsync(tenantSlug, cancellationToken)))
            .WithName("LegacyListAuditEvents");

        tenantAdminGroup.MapGet("/tenants/{tenantSlug}/saas/overview",
            async (string tenantSlug, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.GetSaasArchitectureOverviewAsync(tenantSlug, cancellationToken)))
            .WithName("LegacyGetSaasArchitectureOverview");

        readerGroup.MapPost("/tenants/{tenantSlug}/users/{externalUserId}/recompute",
            async (string tenantSlug, string externalUserId, RecomputeUserContextRequest request, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteAcceptedAsync(() => service.QueueContextRecomputeAsync(
                    new QueueContextRecomputeInput(tenantSlug, externalUserId, request.TriggeredBy),
                    cancellationToken)))
            .WithName("LegacyQueueContextRecompute");

        tenantAdminGroup.MapGet("/connectors/plugins",
            async (IScoutService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetConnectorPluginsAsync(cancellationToken)))
            .WithName("LegacyListConnectorPlugins");

        tenantAdminGroup.MapPost("/connectors/register",
            async (RegisterConnectorInput input, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.RegisterConnectorAsync(input, cancellationToken)))
            .WithName("LegacyRegisterConnector");

        tenantAdminGroup.MapPost("/connectors/validate",
            async (ValidateConnectorConfigurationInput input, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.ValidateConnectorConfigurationAsync(input, cancellationToken)))
            .WithName("LegacyValidateConnectorConfiguration");

        tenantAdminGroup.MapPost("/connectors/health",
            async (CheckConnectorHealthInput input, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.CheckConnectorHealthAsync(input, cancellationToken)))
            .WithName("LegacyCheckConnectorHealth");

        tenantAdminGroup.MapPost("/blueprints/upload",
            async (UploadBlueprintInput input, IBlueprintImportService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.UploadAsync(input, cancellationToken)))
            .WithName("LegacyUploadBlueprint");

        tenantAdminGroup.MapPost("/blueprints/validate",
            async (BlueprintImportInput input, IBlueprintImportService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.ValidateAsync(input, cancellationToken)))
            .WithName("LegacyValidateBlueprint");

        tenantAdminGroup.MapPost("/blueprints/preview",
            async (BlueprintImportInput input, IBlueprintImportService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.PreviewAsync(input, cancellationToken)))
            .WithName("LegacyPreviewBlueprint");

        tenantAdminGroup.MapPost("/blueprints/import",
            async (BlueprintImportInput input, IBlueprintImportService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.ImportAsync(input, cancellationToken)))
            .WithName("LegacyImportBlueprint");

        tenantAdminGroup.MapPost("/selectors/preview",
            async (PreviewSelectorInput input, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.PreviewSelectorAsync(input, cancellationToken)))
            .WithName("LegacyPreviewSelector");

        tenantAdminGroup.MapPost("/selectors/validate",
            async (ValidateSelectorInput input, IScoutService service, CancellationToken cancellationToken) =>
                await ExecuteRequiredAsync(() => service.ValidateSelectorAsync(input, cancellationToken)))
            .WithName("LegacyValidateSelector");

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync<TResponse>(Func<Task<TResponse?>> action)
    {
        try
        {
            var result = await action();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(ToDictionary(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ExecuteRequiredAsync<TResponse>(Func<Task<TResponse>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(ToDictionary(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ExecuteAcceptedAsync<TResponse>(Func<Task<TResponse>> action)
    {
        try
        {
            var result = await action();
            return Results.Accepted(value: result);
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(ToDictionary(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static Dictionary<string, string[]> ToDictionary(ValidationException exception)
    {
        return exception.Errors
            .GroupBy(error => string.IsNullOrWhiteSpace(error.PropertyName) ? "request" : error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());
    }
}
