using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.OpenApi;
using KynticAI.Scout.Api.Auth;
using KynticAI.Scout.Api.GraphQL;
using KynticAI.Scout.Api.Middleware;
using KynticAI.Scout.Api.Onboarding;
using KynticAI.Scout.Api.Rest;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Infrastructure;
using KynticAI.Scout.Infrastructure.Auth;
using KynticAI.Scout.Infrastructure.Configuration;
using KynticAI.Scout.Infrastructure.Persistence;
using KynticAI.Scout.Infrastructure.Seed;
using FluentValidation;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Authorization;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var platformOptions = builder.Configuration.GetSection(PlatformOptions.SectionName).Get<PlatformOptions>() ?? new PlatformOptions();
var featureFlagOptions = builder.Configuration.GetSection(FeatureFlagOptions.SectionName).Get<FeatureFlagOptions>() ?? new FeatureFlagOptions();
var bootstrapOptions = builder.Configuration.GetSection(BootstrapOptions.SectionName).Get<BootstrapOptions>() ?? new BootstrapOptions();
var connectorBootstrapOptions = builder.Configuration.GetSection(ConnectorBootstrapOptions.SectionName).Get<ConnectorBootstrapOptions>() ?? new ConnectorBootstrapOptions();
var controlPlaneOptions = builder.Configuration.GetSection(ControlPlaneOptions.SectionName).Get<ControlPlaneOptions>() ?? new ControlPlaneOptions();
var licenceOptions = builder.Configuration.GetSection(LicenceOptions.SectionName).Get<LicenceOptions>() ?? new LicenceOptions();
var configuredAuthOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
var telemetryOptions = builder.Configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new TelemetryOptions();
var rateLimitOptions = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
var graphQlOptions = builder.Configuration.GetSection(GraphQlOptions.SectionName).Get<GraphQlOptions>() ?? new GraphQlOptions();
var securityHeadersOptions = builder.Configuration.GetSection(SecurityHeadersOptions.SectionName).Get<SecurityHeadersOptions>() ?? new SecurityHeadersOptions();
var hostedMode = builder.Environment.IsProduction()
    || string.Equals(platformOptions.Mode, PlatformModes.ManagedDataPlane, StringComparison.OrdinalIgnoreCase)
    || string.Equals(platformOptions.Mode, PlatformModes.SaaS, StringComparison.OrdinalIgnoreCase);

// Runtime/deployment mode and optional commercial features are deliberately
// independent. Enabling a production data plane must never silently enable
// control-plane contact or usage/billing reporting.

var productionReadinessReport = ProductionEnvironmentReadinessValidator.GetReport(builder.Configuration, builder.Environment);
ProductionEnvironmentReadinessValidator.ThrowIfBlocked(productionReadinessReport);

if (hostedMode
    && configuredAuthOptions.RequireSecureSigningKey
    && (string.IsNullOrWhiteSpace(configuredAuthOptions.SigningKey)
        || configuredAuthOptions.SigningKey.Contains("development-only", StringComparison.OrdinalIgnoreCase)
        || configuredAuthOptions.SigningKey.Contains("change", StringComparison.OrdinalIgnoreCase)
        || configuredAuthOptions.SigningKey.Contains("replace", StringComparison.OrdinalIgnoreCase)
        || configuredAuthOptions.SigningKey.Length < configuredAuthOptions.MinimumSigningKeyLength))
{
    throw new InvalidOperationException("Auth:SigningKey must be set to a high-entropy production secret before running a production data plane.");
}

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (hostedMode ? [] : ["http://localhost:5173", "http://127.0.0.1:5173"]);

allowedOrigins = allowedOrigins
    .Where(static origin => !string.IsNullOrWhiteSpace(origin))
    .Select(static origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
ValidateAllowedOrigins(allowedOrigins, hostedMode);

builder.Services.Configure<GraphQlOptions>(builder.Configuration.GetSection(GraphQlOptions.SectionName));
builder.Services.Configure<SecurityHeadersOptions>(builder.Configuration.GetSection(SecurityHeadersOptions.SectionName));
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebApp", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    var configuredKnownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    foreach (var configuredProxy in configuredKnownProxies.Where(static value => !string.IsNullOrWhiteSpace(value)))
    {
        if (!IPAddress.TryParse(configuredProxy.Trim(), out var proxyAddress))
        {
            throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains invalid IP address '{configuredProxy}'.");
        }

        options.KnownProxies.Add(proxyAddress);
    }
});
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.Secure = hostedMode ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
    options.HttpOnly = HttpOnlyPolicy.Always;
});
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "ScoutAuth";
        options.DefaultChallengeScheme = "ScoutAuth";
    })
    .AddPolicyScheme("ScoutAuth", "JWT bearer or API key", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorization = context.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(context.Request.Headers[ApiKeyAuthenticationHandler.ApiKeyHeaderName].FirstOrDefault())
                || (!string.IsNullOrWhiteSpace(authorization) && authorization.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase)))
            {
                return ApiKeyAuthenticationHandler.SchemeName;
            }

            return JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddJwtBearer(options =>
    {
        var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = authOptions.Issuer,
            ValidAudience = authOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        options => { });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT bearer token issued by /api/auth/login or /api/auth/token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Machine client API key. Send X-API-Client-Id and X-API-Key, or Authorization: ApiKey {clientId}:{apiKey}.",
        Name = ApiKeyAuthenticationHandler.ApiKeyHeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy<string>("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, rateLimitOptions.AuthPermitLimit),
                Window = TimeSpan.FromSeconds(Math.Max(1, rateLimitOptions.AuthWindowSeconds)),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
    options.AddPolicy<string>("graphql", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            GetRateLimitPartitionKey(context),
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, rateLimitOptions.GraphQlTokenLimit),
                TokensPerPeriod = Math.Max(1, rateLimitOptions.GraphQlTokensPerPeriod),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                ReplenishmentPeriod = TimeSpan.FromSeconds(Math.Max(1, rateLimitOptions.GraphQlReplenishmentSeconds)),
                AutoReplenishment = true
            }));
});

builder.Services
    .AddScoutApplication()
    .AddScoutInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<OnboardingAccessGuard>();

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddType<DateTimeType>()
    .AddAuthorization()
    .AddMaxExecutionDepthRule(
        Math.Max(1, graphQlOptions.MaxExecutionDepth),
        skipIntrospectionFields: true,
        allowRequestOverrides: false,
        (_, _) => true)
    .DisableIntrospection(hostedMode && graphQlOptions.DisableIntrospectionInProduction)
    .ModifyRequestOptions(options =>
    {
        options.IncludeExceptionDetails = builder.Environment.IsDevelopment();
        options.ExecutionTimeout = TimeSpan.FromSeconds(Math.Clamp(graphQlOptions.ExecutionTimeoutSeconds, 1, 120));
    })
    .ModifyServerOptions(options =>
    {
        options.EnableGetRequests = !hostedMode || graphQlOptions.EnableGetRequestsInProduction;
        options.EnableSchemaRequests = !hostedMode || graphQlOptions.EnableSchemaRequestsInProduction;
        options.EnableMultipartRequests = graphQlOptions.EnableMultipartRequests;
        options.MaxBatchSize = Math.Max(1, graphQlOptions.MaxBatchSize);
        options.MaxConcurrentExecutions = Math.Max(1, graphQlOptions.MaxConcurrentExecutions);
    })
    .AddErrorFilter<GraphQlErrorFilter>();

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: string.IsNullOrWhiteSpace(telemetryOptions.ServiceName) ? "KynticAI.Scout.Api" : telemetryOptions.ServiceName,
        serviceNamespace: string.IsNullOrWhiteSpace(telemetryOptions.ServiceNamespace) ? "KynticAIScout" : telemetryOptions.ServiceNamespace)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = string.IsNullOrWhiteSpace(telemetryOptions.DeploymentEnvironment)
                ? builder.Environment.EnvironmentName
                : telemetryOptions.DeploymentEnvironment
        }))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("KynticAI.Scout.Ai");

        var otlpEndpoint = builder.Configuration["Telemetry:OtlpEndpoint"]
            ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("KynticAI.Scout.BackgroundJobs");

        var otlpEndpoint = builder.Configuration["Telemetry:OtlpEndpoint"]
            ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

var app = builder.Build();
var migrateOnly = BootstrapCommandResolver.IsMigrationCommand(args);
var seedDemoOnly = BootstrapCommandResolver.IsSeedDemoCommand(args);
var bootstrapOnly = migrateOnly || seedDemoOnly;

if ((seedDemoOnly || bootstrapOptions.SeedDemoData)
    && hostedMode
    && !string.Equals(platformOptions.Mode, PlatformModes.LocalDemo, StringComparison.OrdinalIgnoreCase)
    && !string.Equals(platformOptions.Mode, "Demo", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Demo seeding is only available in LocalDemo/Demo mode. Production data-plane deployments must run without demo seed data.");
}

var resolvedBootstrapOptions = BootstrapCommandResolver.Resolve(
    bootstrapOptions,
    explicitMigrationCommand: migrateOnly,
    explicitSeedDemoCommand: seedDemoOnly);

app.UseExceptionHandler();
app.UseForwardedHeaders();
if (securityHeadersOptions.Enabled)
{
    var docsPrefixes = platformOptions.EnableOpenApi && platformOptions.EnableRest
        ? new[] { "/api-docs", "/swagger" }
        : Array.Empty<string>();

    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = securityHeadersOptions.ReferrerPolicy;
        context.Response.Headers["Permissions-Policy"] = securityHeadersOptions.PermissionsPolicy;
        context.Response.Headers["X-Frame-Options"] = securityHeadersOptions.FrameOptions;
        if (!string.IsNullOrWhiteSpace(securityHeadersOptions.ContentSecurityPolicy))
        {
            var isDocsPath = docsPrefixes.Any(p =>
                context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

            if (!isDocsPath)
            {
                context.Response.Headers["Content-Security-Policy"] = securityHeadersOptions.ContentSecurityPolicy;
            }
        }

        await next();
    });
}

if (hostedMode && securityHeadersOptions.EnableHsts)
{
    app.UseHsts();
}

app.UseCookiePolicy();
app.UseMiddleware<RequestContextMiddleware>();
await ApplicationBootstrapper.InitializeAsync(app.Services, resolvedBootstrapOptions, connectorBootstrapOptions);
if (bootstrapOnly)
{
    return;
}

if (platformOptions.EnableOpenApi && platformOptions.EnableRest)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "KynticAI Scout REST API v1");
        options.RoutePrefix = "swagger";
    });
    app.MapScalarApiReference("/api-docs", options =>
    {
        options
            .WithTitle("KynticAI Scout API")
            .ExpandAllTags()
            .AddDocument("v1", routePattern: "/swagger/v1/swagger.json");
    });
}

app.UseCors("WebApp");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<PermissionDeniedAuditMiddleware>();
app.UseAuthorization();

var authGroup = app.MapGroup("/api/auth");
authGroup.MapPost("/login", async (
        LoginRequest request,
        AuthenticationService authenticationService,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var result = await authenticationService.LoginAsync(request.TenantSlug, request.Email, request.Password, cancellationToken);
            return Results.Ok(new AuthSessionResponse(
                result.AccessToken,
                result.ExpiresAtUtc,
                new AuthenticatedOperatorResponse(
                    result.Operator.TenantId,
                    result.Operator.TenantSlug,
                    result.Operator.WorkspaceId,
                    result.Operator.WorkspaceSlug,
                    result.Operator.OperatorAccountId,
                    result.Operator.Email,
                    result.Operator.DisplayName,
                    result.Operator.Role)));
        }
        catch (InvalidOperationException)
        {
            return Results.Unauthorized();
        }
    })
    .AllowAnonymous()
    .RequireRateLimiting("auth");

authGroup.MapPost("/token", async (
        HttpRequest httpRequest,
        MachineClientAuthenticationService authenticationService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        MachineTokenRequest request;
        try
        {
            request = await ReadMachineTokenRequestAsync(httpRequest, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or BadHttpRequestException or InvalidDataException)
        {
            return Results.Problem(
                title: "Invalid token request",
                detail: "The token request body is missing or malformed.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(request.GrantType, "client_credentials", StringComparison.Ordinal))
        {
            return Results.Problem(
                title: "Unsupported grant type",
                detail: "Only the OAuth 2.0 client_credentials flow is supported.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await authenticationService.AuthenticateAsync(
                request.ClientId,
                request.ClientSecret,
                request.Scope,
                cancellationToken);
            var expiresIn = Math.Max(1, (int)Math.Ceiling((result.ExpiresAtUtc - timeProvider.GetUtcNow().UtcDateTime).TotalSeconds));
            return Results.Ok(new MachineTokenResponse(
                result.AccessToken,
                "Bearer",
                expiresIn,
                string.Join(' ', result.GrantedScopes)));
        }
        catch (InvalidOperationException)
        {
            return Results.Problem(
                title: "Invalid client credentials",
                detail: "Client authentication failed.",
                statusCode: StatusCodes.Status401Unauthorized);
        }
    })
    .AllowAnonymous()
    .RequireRateLimiting("auth");

authGroup.MapGet("/me", [Microsoft.AspNetCore.Authorization.Authorize] async (
        HttpContext httpContext,
        AuthenticationService authenticationService,
        CancellationToken cancellationToken) =>
    {
        var currentOperator = await authenticationService.GetCurrentOperatorAsync(httpContext.User, cancellationToken);
        return currentOperator is null
            ? Results.Unauthorized()
            : Results.Ok(new AuthenticatedOperatorResponse(
                currentOperator.TenantId,
                currentOperator.TenantSlug,
                currentOperator.WorkspaceId,
                currentOperator.WorkspaceSlug,
                currentOperator.OperatorAccountId,
                currentOperator.Email,
                currentOperator.DisplayName,
                currentOperator.Role));
    });

var apiClientsGroup = authGroup.MapGroup("/api-clients")
    .RequireAuthorization(new AuthorizeAttribute
    {
        Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin, RoleNames.IntegrationAdmin)
    });
apiClientsGroup.MapGet("/", async (
        string tenantSlug,
        ApiClientKeyService apiClientKeyService,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var clients = await apiClientKeyService.ListAsync(tenantSlug, cancellationToken);
            return Results.Ok(clients.Select(client => new ApiClientSummaryResponse(
                client.Id,
                client.TenantId,
                client.WorkspaceId,
                client.ClientId,
                client.DisplayName,
                client.Status,
                client.Scopes,
                client.LastUsedAtUtc,
                client.RotatedAtUtc,
                client.RevokedAtUtc)));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    });
apiClientsGroup.MapPost("/", async (
        CreateApiClientRequest request,
        ApiClientKeyService apiClientKeyService,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var result = await apiClientKeyService.CreateAsync(
                request.TenantSlug,
                request.WorkspaceSlug,
                request.DisplayName,
                request.Scopes,
                cancellationToken);
            return Results.Created($"/api/auth/api-clients/{result.ClientId}", new ApiClientCreatedResponse(
                result.Id,
                result.TenantId,
                result.WorkspaceId,
                result.ClientId,
                result.DisplayName,
                result.ApiKey,
                result.Scopes,
                result.CreatedAtUtc));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    });
apiClientsGroup.MapPost("/{clientId}/rotate", async (
        string clientId,
        RotateApiClientRequest request,
        ApiClientKeyService apiClientKeyService,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var result = await apiClientKeyService.RotateAsync(request.TenantSlug, clientId, cancellationToken);
            return Results.Ok(new ApiClientRotatedResponse(
                result.Id,
                result.ClientId,
                result.ApiKey,
                result.RotatedAtUtc));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    });
apiClientsGroup.MapPost("/{clientId}/revoke", async (
        string clientId,
        RotateApiClientRequest request,
        ApiClientKeyService apiClientKeyService,
        CancellationToken cancellationToken) =>
    {
        try
        {
            await apiClientKeyService.RevokeAsync(request.TenantSlug, clientId, cancellationToken);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    });

app.MapPost("/api/onboarding", async (
        SubmitOnboardingInput request,
        HttpContext httpContext,
        OnboardingAccessGuard onboardingAccessGuard,
        IOnboardingService onboardingService,
        CancellationToken cancellationToken) =>
    {
        var disabled = onboardingAccessGuard.DenyAnonymousIfDisabled(httpContext, request.TenantSlug, "rest");
        if (disabled is not null)
        {
            return disabled;
        }

        try
        {
            var result = await onboardingService.SubmitAsync(request, cancellationToken);
            return Results.Created($"/api/onboarding/{result.OnboardingApplicationId}", result);
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(ToValidationErrors(exception));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                title: "Onboarding could not be completed",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    })
    .AllowAnonymous()
    .RequireRateLimiting("auth")
    .WithName("SubmitOnboarding");

var opsGroup = app.MapGroup("/api/ops")
    .RequireAuthorization(new AuthorizeAttribute { Roles = string.Join(',', RoleNames.PlatformOwner, RoleNames.TenantAdmin) });
opsGroup.MapGet("/summary", async (
        ScoutDbContext dbContext,
        ICurrentActorService currentActorService,
        IBackgroundJobMonitor backgroundJobMonitor,
        CancellationToken cancellationToken) =>
    {
        var actor = currentActorService.GetCurrentActor();
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Slug == actor.TenantSlug, cancellationToken);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        var activeAgentRuns = await dbContext.AgentRuns.CountAsync(
            x => x.TenantId == tenant.Id && x.RequestedAtUtc >= DateTime.UtcNow.AddDays(-1),
            cancellationToken);
        var failedAgentRuns = await dbContext.AgentRuns.CountAsync(
            x => x.TenantId == tenant.Id && x.Status == KynticAI.Scout.Domain.Enums.AgentRunStatus.Failed,
            cancellationToken);
        var pendingExecutions = await dbContext.SelectorExecutions.CountAsync(
            x => x.TenantId == tenant.Id && (x.Status == KynticAI.Scout.Domain.Enums.SelectorExecutionStatus.Pending || x.Status == KynticAI.Scout.Domain.Enums.SelectorExecutionStatus.Running),
            cancellationToken);
        var staleSnapshots = await dbContext.ContextSnapshots.CountAsync(
            x => x.TenantId == tenant.Id && x.IsStale,
            cancellationToken);

        return Results.Ok(new
        {
            tenant = tenant.Slug,
            backgroundWorkers = backgroundJobMonitor.GetWorkers(),
            stats = new
            {
                activeAgentRuns,
                failedAgentRuns,
                pendingExecutions,
                staleSnapshots
            }
        });
    });
opsGroup.MapGet("/production-readiness", () => Results.Ok(productionReadinessReport));

if (platformOptions.EnableRest)
{
    app.MapScoutRestApi();
    app.MapScoutV1RestApi();
}

app.MapGet("/", () =>
{
    if (platformOptions.EnableOpenApi && platformOptions.EnableRest)
    {
        return Results.Redirect("/api-docs");
    }

    if (platformOptions.EnableGraphQl)
    {
        return Results.Redirect("/graphql");
    }

    return Results.Ok(new
    {
        service = "KynticAI.Scout.Api",
        mode = platformOptions.Mode,
        rest = platformOptions.EnableRest,
        graphql = platformOptions.EnableGraphQl
    });
});
app.MapGet("/health", async (ScoutDbContext scoutDbContext, CancellationToken cancellationToken) =>
{
    var scoutReady = await scoutDbContext.Database.CanConnectAsync(cancellationToken);
    return Results.Ok(new
    {
        status = scoutReady ? "ok" : "degraded",
        service = "KynticAI.Scout.Api",
        checks = new[]
        {
            new { name = "self", status = "ok" },
            new { name = "scout-db", status = scoutReady ? "ok" : "error" }
        }
    });
});
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "ok",
    service = "KynticAI.Scout.Api"
}));
var platformConfigEndpoint = app.MapGet("/api/platform/config", () => Results.Ok(new
{
    service = "KynticAI.Scout.Api",
    mode = platformOptions.Mode,
    features = featureFlagOptions.EnabledFlags(),
    onboarding = new
    {
        anonymousEnabled = app.Services.GetRequiredService<OnboardingAccessGuard>().IsAnonymousOnboardingAllowed()
    },
    controlPlane = new
    {
        enabled = controlPlaneOptions.Enabled,
        baseUrl = controlPlaneOptions.BaseUrl,
        updateChannel = controlPlaneOptions.UpdateChannel,
        usageReportingEnabled = controlPlaneOptions.UsageReportingEnabled,
        offlineGracePeriodDays = controlPlaneOptions.OfflineGracePeriodDays
    },
    licence = new
    {
        mode = licenceOptions.Mode,
        requireValidLicence = licenceOptions.RequireValidLicence,
        offlineGracePeriodDays = licenceOptions.OfflineGracePeriodDays
    },
    endpoints = new
    {
        graphql = platformOptions.EnableGraphQl,
        rest = platformOptions.EnableRest,
        openApi = platformOptions.EnableOpenApi
    }
}));
if (hostedMode)
{
    platformConfigEndpoint.RequireAuthorization();
}
app.MapGet("/health/ready", async (ScoutDbContext scoutDbContext, CancellationToken cancellationToken) =>
{
    var scoutReady = await scoutDbContext.Database.CanConnectAsync(cancellationToken);
    return scoutReady
        ? Results.Ok(new
        {
            status = "ok",
            service = "KynticAI.Scout.Api",
            checks = new[]
            {
                new { name = "scout-db", status = "ok" }
            }
        })
        : Results.Problem(
            title: "Database unavailable",
            detail: "The API cannot reach the configured Scout database.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
});
if (platformOptions.EnableGraphQl)
{
    app.MapGraphQL("/graphql").RequireAuthorization().RequireRateLimiting("graphql");
}

app.Run();

static async Task<MachineTokenRequest> ReadMachineTokenRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken)
{
    MachineTokenRequest request;
    if (httpRequest.HasFormContentType)
    {
        var form = await httpRequest.ReadFormAsync(cancellationToken);
        request = new MachineTokenRequest(
            form["grant_type"].ToString(),
            form["client_id"].ToString(),
            form["client_secret"].ToString(),
            form["scope"].ToString());
    }
    else
    {
        request = await httpRequest.ReadFromJsonAsync<MachineTokenRequest>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("A machine token request body is required.");
    }

    if (string.IsNullOrWhiteSpace(request.GrantType)
        || string.IsNullOrWhiteSpace(request.ClientId)
        || string.IsNullOrWhiteSpace(request.ClientSecret)
        || request.GrantType.Length > 64
        || request.ClientId.Length > 256
        || request.ClientSecret.Length > 4_096
        || (request.Scope?.Length ?? 0) > 4_096)
    {
        throw new InvalidOperationException("The machine token request is incomplete or exceeds accepted bounds.");
    }

    return request with
    {
        GrantType = request.GrantType.Trim(),
        ClientId = request.ClientId.Trim(),
        Scope = string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim()
    };
}

static Dictionary<string, string[]> ToValidationErrors(ValidationException exception)
    => exception.Errors
        .GroupBy(error => error.PropertyName)
        .ToDictionary(
            group => group.Key,
            group => group.Select(error => error.ErrorMessage).ToArray());

static void ValidateAllowedOrigins(string[] origins, bool hostedMode)
{
    if (hostedMode && origins.Length == 0)
    {
        throw new InvalidOperationException("Cors:AllowedOrigins must list the production frontend origin before running a production data plane.");
    }

    foreach (var origin in origins)
    {
        if (!CorsOriginValidator.TryValidate(origin, hostedMode, out var error))
        {
            throw new InvalidOperationException($"Cors:AllowedOrigins contains invalid origin '{origin}': {error}.");
        }
    }
}

static string GetRateLimitPartitionKey(HttpContext context)
    => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;
