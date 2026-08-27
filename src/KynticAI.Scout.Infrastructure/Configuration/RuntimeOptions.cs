using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Enums;

namespace KynticAI.Scout.Infrastructure.Configuration;

public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    public string Mode { get; set; } = PlatformModes.BackendOnly;

    public bool EnableGraphQl { get; set; } = true;

    public bool EnableRest { get; set; } = true;

    public bool EnableOpenApi { get; set; } = true;
}

public static class PlatformModes
{
    public const string LocalDemo = "LocalDemo";

    /// <summary>
    /// Customer-controlled production/data-plane deployment.
    /// </summary>
    public const string SelfHosted = "SelfHosted";

    /// <summary>
    /// Production data plane operated as a managed deployment. This does not
    /// imply that the optional KynticAI commercial control plane is enabled.
    /// </summary>
    public const string ManagedDataPlane = "ManagedDataPlane";

    // Compatibility aliases retained for existing configuration. New
    // deployments should use SelfHosted or ManagedDataPlane.
    public const string BackendOnly = "BackendOnly";

    public const string SaaS = "SaaS";

    public static bool IsProductionDataPlane(string? mode)
        => string.Equals(mode, SelfHosted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, ManagedDataPlane, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, BackendOnly, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, SaaS, StringComparison.OrdinalIgnoreCase);
}

public sealed class FeatureFlagOptions
{
    public const string SectionName = "FeatureFlags";

    public bool DemoExperience { get; set; } = true;

    public bool OpenCoreApis { get; set; } = true;

    public bool SaaSControlPlane { get; set; } = false;

    public bool HostedBillingUsage { get; set; } = false;

    public bool Webhooks { get; set; } = false;

    public bool EnterpriseConnectorExtensions { get; set; } = false;

    public bool AnonymousOnboarding { get; set; } = false;

    public bool AllowProductionOnboarding { get; set; } = false;

    public IReadOnlyList<string> EnabledFlags()
    {
        var flags = new List<string>();
        AddIf(flags, DemoExperience, "demo-experience");
        AddIf(flags, OpenCoreApis, "open-core-apis");
        AddIf(flags, SaaSControlPlane, "saas-control-plane");
        AddIf(flags, HostedBillingUsage, "hosted-billing-usage");
        AddIf(flags, Webhooks, "webhooks");
        AddIf(flags, EnterpriseConnectorExtensions, "enterprise-connector-extensions");
        AddIf(flags, AnonymousOnboarding, "anonymous-onboarding");
        AddIf(flags, AllowProductionOnboarding, "production-onboarding");
        return flags;
    }

    private static void AddIf(List<string> flags, bool enabled, string key)
    {
        if (enabled)
        {
            flags.Add(key);
        }
    }
}

public sealed class SaaSOptions
{
    public const string SectionName = "SaaS";

    public string PublicBaseUrl { get; set; } = "http://127.0.0.1:5198";

    public string ApiIssuer { get; set; } = "KynticAI.Scout.SaaS";

    public bool RequireWorkspaceScope { get; set; } = false;

    public bool PersistApiClients { get; set; } = true;
}

public sealed class ControlPlaneOptions
{
    public const string SectionName = "ControlPlane";

    public bool Enabled { get; set; } = false;

    public string BaseUrl { get; set; } = string.Empty;

    public string CustomerAccountId { get; set; } = string.Empty;

    public string DataPlaneInstallationId { get; set; } = string.Empty;

    public string DeploymentName { get; set; } = string.Empty;

    public string DeploymentVersion { get; set; } = string.Empty;

    public string DeploymentRegion { get; set; } = string.Empty;

    public string EnvironmentType { get; set; } = "SelfHostedCommunity";

    public string UpdateChannel { get; set; } = "stable";

    public bool UsageReportingEnabled { get; set; } = false;

    public int OfflineGracePeriodDays { get; set; } = 30;

    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class LicenceOptions
{
    public const string SectionName = "Licence";

    public string Mode { get; set; } = "Community";

    public string FilePath { get; set; } = ".demo-data/scout-demo.licence.json";

    public string PublicKeyPem { get; set; } = string.Empty;

    public bool RequireValidLicence { get; set; } = false;

    public int OfflineGracePeriodDays { get; set; } = 30;
}

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public bool ApplyMigrationsOnStartup { get; set; } = true;

    public bool SeedDemoData { get; set; } = false;
}

public sealed class DataProtectionKeyOptions
{
    public const string SectionName = "DataProtection";

    public string ApplicationName { get; set; } = "KynticAIScout";

    public string KeyRingPath { get; set; } = string.Empty;

    public bool RequirePersistentKeys { get; set; } = false;
}

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public string ServiceName { get; set; } = "KynticAI.Scout.Api";

    public string ServiceNamespace { get; set; } = "KynticAIScout";

    public string DeploymentEnvironment { get; set; } = "local";

    public string OtlpEndpoint { get; set; } = string.Empty;
}

public sealed class GraphQlOptions
{
    public const string SectionName = "GraphQl";

    public int MaxExecutionDepth { get; set; } = 16;

    public int ExecutionTimeoutSeconds { get; set; } = 15;

    public bool DisableIntrospectionInProduction { get; set; } = true;

    public bool EnableGetRequestsInProduction { get; set; } = false;

    public bool EnableSchemaRequestsInProduction { get; set; } = false;

    public bool EnableMultipartRequests { get; set; } = false;

    public int MaxBatchSize { get; set; } = 5;

    public int MaxConcurrentExecutions { get; set; } = 32;
}

public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    public bool Enabled { get; set; } = true;

    public bool EnableHsts { get; set; } = true;

    public string ContentSecurityPolicy { get; set; } =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    public string PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=(), payment=()";

    public string FrameOptions { get; set; } = "DENY";
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public int AuthPermitLimit { get; set; } = 5;

    public int AuthWindowSeconds { get; set; } = 60;

    public int GraphQlTokenLimit { get; set; } = 60;

    public int GraphQlTokensPerPeriod { get; set; } = 60;

    public int GraphQlReplenishmentSeconds { get; set; } = 60;
}

public sealed class ConnectorBootstrapOptions
{
    public const string SectionName = "ConnectorBootstrap";

    public bool Enabled { get; set; } = false;

    public List<ConnectorBootstrapDefinition> Definitions { get; set; } = [];
}

public sealed class ConnectorBootstrapDefinition
{
    public string TenantSlug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DataSourceKind Kind { get; set; } = DataSourceKind.Crm;

    public string ConnectorType { get; set; } = string.Empty;

    public string ConfigurationJson { get; set; } = "{}";

    public string? CredentialsJson { get; set; }
}

public sealed class StorageAdapterOptions
{
    public const string SectionName = "StorageAdapter";

    public string Provider { get; set; } = StorageAdapterProviderKeys.ScoutPostgres;

    public string VectorProvider { get; set; } = StorageAdapterProviderKeys.Disabled;

    public bool EnableEnterpriseRuntime { get; set; } = false;

    public bool EnableVectorWrites { get; set; } = false;

    public bool EnableDualWrite { get; set; } = false;

    public bool AllowCloudDataMovement { get; set; } = false;

    public string EnterpriseRuntimeBaseUrl { get; set; } = string.Empty;

    public int ExpectedEmbeddingDimensions { get; set; } = 384;

    public string ExportCheckpointPath { get; set; } = ".demo-data/storage-adapter-checkpoints";
}
