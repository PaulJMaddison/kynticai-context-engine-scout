using KynticAI.Scout.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KynticAI.Scout.Infrastructure.Configuration;

public sealed record ProductionReadinessCheck(
    string Key,
    string Status,
    bool BlocksProduction,
    string Message,
    string Evidence);

public sealed record ProductionReadinessReport(
    bool ProductionShapeRequired,
    bool ReadyForProductionStyleDeployment,
    IReadOnlyList<ProductionReadinessCheck> Checks);

public static class ProductionEnvironmentReadinessValidator
{
    private const string Ready = "Ready";
    private const string Warning = "Warning";
    private const string Blocked = "Blocked";

    public static ProductionReadinessReport GetReport(IConfiguration configuration, IHostEnvironment environment)
    {
        var platform = configuration.GetSection(PlatformOptions.SectionName).Get<PlatformOptions>() ?? new PlatformOptions();
        var featureFlags = configuration.GetSection(FeatureFlagOptions.SectionName).Get<FeatureFlagOptions>() ?? new FeatureFlagOptions();
        var bootstrap = configuration.GetSection(BootstrapOptions.SectionName).Get<BootstrapOptions>() ?? new BootstrapOptions();
        var dataProtection = configuration.GetSection(DataProtectionKeyOptions.SectionName).Get<DataProtectionKeyOptions>() ?? new DataProtectionKeyOptions();
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        var controlPlane = configuration.GetSection(ControlPlaneOptions.SectionName).Get<ControlPlaneOptions>() ?? new ControlPlaneOptions();
        var productionShapeRequired = environment.IsProduction()
            || PlatformModes.IsProductionDataPlane(platform.Mode);

        var checks = new List<ProductionReadinessCheck>
        {
            PlatformModeCheck(platform, productionShapeRequired),
            DatabaseProviderCheck(configuration, productionShapeRequired),
            ConnectionStringsCheck(configuration, productionShapeRequired),
            DemoFallbackCheck(configuration, productionShapeRequired),
            DemoSeedCheck(bootstrap, productionShapeRequired),
            DemoExperienceCheck(featureFlags, productionShapeRequired),
            DataProtectionCheck(dataProtection, productionShapeRequired),
            AuthSigningKeyCheck(auth, productionShapeRequired),
            ControlPlaneTransportCheck(controlPlane, productionShapeRequired),
            OpenApiExposureCheck(platform, productionShapeRequired),
            CorsOriginsCheck(configuration, productionShapeRequired),
            SecurityHeadersCheck(configuration, productionShapeRequired)
        };

        return new ProductionReadinessReport(
            productionShapeRequired,
            checks.All(check => !check.BlocksProduction || check.Status != Blocked),
            checks);
    }

    public static void ThrowIfBlocked(ProductionReadinessReport report)
    {
        if (!report.ProductionShapeRequired || report.ReadyForProductionStyleDeployment)
        {
            return;
        }

        var blockers = report.Checks
            .Where(check => check.BlocksProduction && check.Status == Blocked)
            .Select(check => $"{check.Key}: {check.Message}")
            .ToArray();
        throw new InvalidOperationException("Production-style data-plane readiness failed: " + string.Join("; ", blockers));
    }

    private static ProductionReadinessCheck PlatformModeCheck(PlatformOptions platform, bool required)
    {
        var validMode = PlatformModes.IsProductionDataPlane(platform.Mode);
        if (!required)
        {
            return Check("platform-mode", Ready, false, "Production shape is not required in this environment.", platform.Mode);
        }

        return validMode
            ? Check("platform-mode", Ready, true, "Platform mode is valid for a production data-plane deployment.", platform.Mode)
            : Check("platform-mode", Blocked, true, "Platform mode must be SelfHosted or ManagedDataPlane (legacy BackendOnly/SaaS remain compatibility aliases) for production-style deployment.", platform.Mode);
    }

    private static ProductionReadinessCheck DatabaseProviderCheck(IConfiguration configuration, bool required)
    {
        var provider = configuration["Database:Provider"] ?? configuration["DATABASE_PROVIDER"] ?? string.Empty;
        var isPostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);
        if (!required)
        {
            return Check("database-provider", Ready, false, "Production shape is not required in this environment.", string.IsNullOrWhiteSpace(provider) ? "(auto)" : provider);
        }

        return isPostgres
            ? Check("database-provider", Ready, true, "PostgreSQL provider is configured.", provider)
            : Check("database-provider", Blocked, true, "Database:Provider must be Postgres for production-style deployment.", string.IsNullOrWhiteSpace(provider) ? "(missing)" : provider);
    }

    private static ProductionReadinessCheck ConnectionStringsCheck(IConfiguration configuration, bool required)
    {
        var scoutConnection = configuration.GetConnectionString("Scout")
            ?? configuration["SCOUT_CONNECTION_STRING"]
            ?? string.Empty;
        var configured = !string.IsNullOrWhiteSpace(scoutConnection);
        var safe = configured
            && !IsSqliteLike(scoutConnection)
            && !IsPlaceholder(scoutConnection);

        if (!required)
        {
            return Check(
                "connection-strings",
                configured ? Ready : Warning,
                false,
                "Production shape is not required in this environment.",
                configured ? "scout-configured" : "missing");
        }

        return safe
            ? Check("connection-strings", Ready, true, "A PostgreSQL Scout connection string is configured.", "scout-configured")
            : Check("connection-strings", Blocked, true, "ConnectionStrings:Scout must be a non-placeholder PostgreSQL connection string.", configured ? "unsafe-or-sqlite" : "missing");
    }

    private static ProductionReadinessCheck DemoFallbackCheck(IConfiguration configuration, bool required)
    {
        var value = configuration["VITE_DEMO_FALLBACK"];
        if (!required)
        {
            return Check("frontend-demo-fallback", Ready, false, "Production shape is not required in this environment.", string.IsNullOrWhiteSpace(value) ? "(not supplied)" : value);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return Check("frontend-demo-fallback", Warning, false, "VITE_DEMO_FALLBACK is not present in API configuration; verify the frontend build separately.", "(not supplied)");
        }

        return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            ? Check("frontend-demo-fallback", Ready, true, "Frontend demo fallback is disabled.", value)
            : Check("frontend-demo-fallback", Blocked, true, "VITE_DEMO_FALLBACK must be false for customer/prod-style deployments.", value);
    }

    private static ProductionReadinessCheck DemoSeedCheck(BootstrapOptions bootstrap, bool required)
    {
        if (!required)
        {
            return Check("demo-seed", Ready, false, "Production shape is not required in this environment.", bootstrap.SeedDemoData.ToString());
        }

        return bootstrap.SeedDemoData
            ? Check("demo-seed", Blocked, true, "Bootstrap:SeedDemoData must be false for production-style deployment.", "true")
            : Check("demo-seed", Ready, true, "Demo seed data is disabled.", "false");
    }

    private static ProductionReadinessCheck DemoExperienceCheck(FeatureFlagOptions featureFlags, bool required)
    {
        if (!required)
        {
            return Check("demo-experience", Ready, false, "Production shape is not required in this environment.", featureFlags.DemoExperience.ToString());
        }

        return featureFlags.DemoExperience
            ? Check("demo-experience", Blocked, true, "FeatureFlags:DemoExperience must be false for customer/prod-style deployments.", "true")
            : Check("demo-experience", Ready, true, "Demo experience flag is disabled.", "false");
    }

    private static ProductionReadinessCheck DataProtectionCheck(DataProtectionKeyOptions dataProtection, bool required)
    {
        if (!required)
        {
            return Check("data-protection-keys", Ready, false, "Production shape is not required in this environment.", string.IsNullOrWhiteSpace(dataProtection.KeyRingPath) ? "(not supplied)" : "configured");
        }

        if (!dataProtection.RequirePersistentKeys)
        {
            return Check("data-protection-keys", Blocked, true, "DataProtection:RequirePersistentKeys must be true for production-style deployment.", "RequirePersistentKeys=false");
        }

        if (string.IsNullOrWhiteSpace(dataProtection.KeyRingPath) || IsEphemeralPath(dataProtection.KeyRingPath))
        {
            return Check("data-protection-keys", Blocked, true, "DataProtection:KeyRingPath must be a persistent mounted path.", string.IsNullOrWhiteSpace(dataProtection.KeyRingPath) ? "(missing)" : dataProtection.KeyRingPath);
        }

        return Check("data-protection-keys", Ready, true, "Persistent Data Protection key path is configured.", "configured");
    }

    private static ProductionReadinessCheck AuthSigningKeyCheck(AuthOptions auth, bool required)
    {
        if (!required || !auth.RequireSecureSigningKey)
        {
            return Check("auth-signing-key", Ready, required, "Secure signing key enforcement is not blocking in this environment.", auth.RequireSecureSigningKey.ToString());
        }

        var minimumLength = Math.Max(48, auth.MinimumSigningKeyLength);
        var safe = !string.IsNullOrWhiteSpace(auth.SigningKey)
            && auth.SigningKey.Length >= minimumLength
            && !IsPlaceholder(auth.SigningKey);

        return safe
            ? Check("auth-signing-key", Ready, true, "Auth signing key is production-shaped.", $"length>={minimumLength}")
            : Check("auth-signing-key", Blocked, true, $"Auth:SigningKey must be a non-placeholder secret of at least {minimumLength} characters.", "missing-placeholder-or-short");
    }

    private static ProductionReadinessCheck ControlPlaneTransportCheck(ControlPlaneOptions controlPlane, bool required)
    {
        if (!controlPlane.Enabled)
        {
            return Check("control-plane-transport", Ready, false, "Cloud control-plane checks are disabled.", "disabled");
        }

        if (!Uri.TryCreate(controlPlane.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrWhiteSpace(baseUri.Host))
        {
            return Check(
                "control-plane-transport",
                required ? Blocked : Warning,
                required,
                "ControlPlane:BaseUrl must be an absolute HTTP(S) URI when the control plane is enabled.",
                "invalid-base-url");
        }

        if (required && baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return Check(
                "control-plane-transport",
                Blocked,
                true,
                "ControlPlane:BaseUrl must use HTTPS for production-style deployments.",
                "insecure-http");
        }

        return Check(
            "control-plane-transport",
            Ready,
            required,
            required ? "Control-plane entitlement transport uses HTTPS." : "Control-plane endpoint is configured for this non-production environment.",
            baseUri.Scheme);
    }

    private static ProductionReadinessCheck OpenApiExposureCheck(PlatformOptions platform, bool required)
    {
        if (!required)
        {
            return Check("openapi-exposure", Ready, false, "Production shape is not required in this environment.", platform.EnableOpenApi.ToString());
        }

        return platform.EnableOpenApi
            ? Check("openapi-exposure", Blocked, true, "Platform:EnableOpenApi must be false for production-style deployments unless deliberately fronted by separate authenticated tooling.", "enabled")
            : Check("openapi-exposure", Ready, true, "OpenAPI/Swagger exposure is disabled by default.", "disabled");
    }

    private static ProductionReadinessCheck CorsOriginsCheck(IConfiguration configuration, bool required)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var normalised = origins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!required)
        {
            return Check("cors-origins", Ready, false, "Production shape is not required in this environment.", normalised.Length == 0 ? "(none)" : string.Join(',', normalised));
        }

        if (normalised.Length == 0)
        {
            return Check("cors-origins", Blocked, true, "Cors:AllowedOrigins must list exact production origins.", "(missing)");
        }

        foreach (var origin in normalised)
        {
            if (!CorsOriginValidator.TryValidate(origin, hostedMode: true, out var error))
            {
                return Check("cors-origins", Blocked, true, $"Cors:AllowedOrigins contains an invalid production origin: {error}.", "invalid-origin");
            }
        }

        return Check("cors-origins", Ready, true, "Exact HTTPS CORS origins are configured.", string.Join(',', normalised));
    }

    private static ProductionReadinessCheck SecurityHeadersCheck(IConfiguration configuration, bool required)
    {
        var options = configuration.GetSection(SecurityHeadersOptions.SectionName).Get<SecurityHeadersOptions>() ?? new SecurityHeadersOptions();
        if (!required)
        {
            return Check("security-headers", Ready, false, "Production shape is not required in this environment.", options.Enabled.ToString());
        }

        if (!options.Enabled)
        {
            return Check("security-headers", Blocked, true, "SecurityHeaders:Enabled must be true for production-style deployments.", "disabled");
        }

        if (string.IsNullOrWhiteSpace(options.ContentSecurityPolicy) || !options.ContentSecurityPolicy.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase))
        {
            return Check("security-headers", Blocked, true, "SecurityHeaders:ContentSecurityPolicy must be configured and include frame-ancestors.", "missing-csp");
        }

        return Check("security-headers", Ready, true, "Security headers are enabled.", "enabled");
    }

    private static ProductionReadinessCheck Check(string key, string status, bool blocksProduction, string message, string evidence) =>
        new(key, status, blocksProduction, message, evidence);

    private static bool IsSqliteLike(string value) =>
        value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Filename=", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholder(string value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Contains("development-only", StringComparison.OrdinalIgnoreCase)
        || value.Contains("replace", StringComparison.OrdinalIgnoreCase)
        || value.Contains("change", StringComparison.OrdinalIgnoreCase)
        || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
        || value.Contains("example", StringComparison.OrdinalIgnoreCase);

    private static bool IsEphemeralPath(string value) =>
        value.Contains(".demo-data", StringComparison.OrdinalIgnoreCase)
        || value.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
        || value.Contains("/tmp/", StringComparison.OrdinalIgnoreCase)
        || value.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase);
}
