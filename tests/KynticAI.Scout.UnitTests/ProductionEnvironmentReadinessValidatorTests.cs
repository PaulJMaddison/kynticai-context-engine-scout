using KynticAI.Scout.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace KynticAI.Scout.UnitTests;

public sealed class ProductionEnvironmentReadinessValidatorTests
{
    [Fact]
    public void Production_shape_passes_when_required_settings_are_safe()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            Configuration(new Dictionary<string, string?>
            {
                ["Platform:Mode"] = PlatformModes.SelfHosted,
                ["Database:Provider"] = "Postgres",
                ["ConnectionStrings:Scout"] = "Host=postgres.internal;Port=5432;Database=scout_context;Username=scout;Password=not-real",
                ["Bootstrap:SeedDemoData"] = "false",
                ["FeatureFlags:DemoExperience"] = "false",
                ["DataProtection:RequirePersistentKeys"] = "true",
                ["DataProtection:KeyRingPath"] = "C:\\scout-data-protection-keys",
                ["Auth:SigningKey"] = new string('a', 64),
                ["Auth:MinimumSigningKeyLength"] = "48",
                ["Auth:RequireSecureSigningKey"] = "true",
                ["Platform:EnableOpenApi"] = "false",
                ["Cors:AllowedOrigins:0"] = "https://app.example.invalid",
                ["SecurityHeaders:Enabled"] = "true",
                ["SecurityHeaders:ContentSecurityPolicy"] = "default-src 'none'; frame-ancestors 'none'",
                ["VITE_DEMO_FALLBACK"] = "false"
            }),
            new TestHostEnvironment("Production"));

        Assert.True(report.ProductionShapeRequired);
        Assert.True(report.ReadyForProductionStyleDeployment);
        Assert.DoesNotContain(report.Checks, check => check.BlocksProduction && check.Status == "Blocked");
    }

    [Fact]
    public void Production_shape_does_not_require_customer_ops_connection_string()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CustomerOps"] = null,
                ["ReferenceData:CustomerOpsEnabled"] = "false"
            }),
            new TestHostEnvironment("Production"));

        Assert.True(report.ReadyForProductionStyleDeployment);
        Assert.DoesNotContain(report.Checks, check =>
            check.Key == "connection-strings"
            && check.Evidence.Contains("customer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_shape_blocks_sqlite_and_demo_fallback()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Scout"] = "Data Source=.demo-data/scout_context.db",
                ["VITE_DEMO_FALLBACK"] = "true"
            }),
            new TestHostEnvironment("Production"));

        Assert.False(report.ReadyForProductionStyleDeployment);
        Assert.Contains(report.Checks, check => check.Key == "database-provider" && check.Status == "Blocked");
        Assert.Contains(report.Checks, check => check.Key == "connection-strings" && check.Status == "Blocked");
        Assert.Contains(report.Checks, check => check.Key == "frontend-demo-fallback" && check.Status == "Blocked");
    }

    [Fact]
    public void Production_shape_blocks_demo_seed_demo_experience_and_missing_data_protection()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["Bootstrap:SeedDemoData"] = "true",
                ["FeatureFlags:DemoExperience"] = "true",
                ["DataProtection:RequirePersistentKeys"] = "false",
                ["DataProtection:KeyRingPath"] = ""
            }),
            new TestHostEnvironment("Production"));

        Assert.False(report.ReadyForProductionStyleDeployment);
        Assert.Contains(report.Checks, check => check.Key == "demo-seed" && check.Status == "Blocked");
        Assert.Contains(report.Checks, check => check.Key == "demo-experience" && check.Status == "Blocked");
        Assert.Contains(report.Checks, check => check.Key == "data-protection-keys" && check.Status == "Blocked");
    }

    [Fact]
    public void Production_shape_blocks_workspace_scope_claim_until_end_to_end_isolation_exists()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["SaaS:RequireWorkspaceScope"] = "true"
            }),
            new TestHostEnvironment("Production"));

        Assert.False(report.ReadyForProductionStyleDeployment);
        Assert.Contains(report.Checks, check =>
            check.Key == "workspace-isolation"
            && check.Status == "Blocked");
    }

    [Fact]
    public void Production_shape_blocks_placeholder_signing_key()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["Auth:SigningKey"] = "replace-with-production-secret"
            }),
            new TestHostEnvironment("Production"));

        Assert.False(report.ReadyForProductionStyleDeployment);
        Assert.Contains(report.Checks, check => check.Key == "auth-signing-key" && check.Status == "Blocked");
    }

    [Fact]
    public void Production_shape_blocks_insecure_control_plane_transport()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["ControlPlane:Enabled"] = "true",
                ["ControlPlane:BaseUrl"] = "http://control-plane.internal/"
            }),
            new TestHostEnvironment("Production"));

        Assert.False(report.ReadyForProductionStyleDeployment);
        Assert.Contains(report.Checks, check => check.Key == "control-plane-transport" && check.Status == "Blocked");
    }

    [Fact]
    public void Production_shape_accepts_https_control_plane_transport()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["ControlPlane:Enabled"] = "true",
                ["ControlPlane:BaseUrl"] = "https://control-plane.example.invalid/"
            }),
            new TestHostEnvironment("Production"));

        Assert.True(report.ReadyForProductionStyleDeployment);
        Assert.Contains(report.Checks, check => check.Key == "control-plane-transport" && check.Status == "Ready");
    }

    [Fact]
    public void Production_shape_blocks_openapi_wildcard_cors_and_missing_security_headers()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["Platform:EnableOpenApi"] = "true",
                ["Cors:AllowedOrigins:0"] = "*",
                ["SecurityHeaders:Enabled"] = "false"
            }),
            new TestHostEnvironment("Production"));

        Assert.False(report.ReadyForProductionStyleDeployment);
        Assert.Contains(report.Checks, check => check.Key == "openapi-exposure" && check.Status == "Blocked");
        Assert.Contains(report.Checks, check => check.Key == "cors-origins" && check.Status == "Blocked");
        Assert.Contains(report.Checks, check => check.Key == "security-headers" && check.Status == "Blocked");
    }

    [Fact]
    public void PlatformOptions_DefaultsToLocalDemo_NotLegacyBackendOnlyAlias()
    {
        Assert.Equal(PlatformModes.LocalDemo, new PlatformOptions().Mode);
    }

    [Fact]
    public void ProductionShape_BlocksFictionalCustomerOpsReferenceDatabase()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>
            {
                ["ReferenceData:CustomerOpsEnabled"] = "true"
            }),
            new TestHostEnvironment("Development"));

        Assert.True(report.ProductionShapeRequired);
        Assert.False(report.ReadyForProductionStyleDeployment);
        var check = Assert.Single(report.Checks.Where(x => x.Key == "customerops-reference-data"));
        Assert.Equal("Blocked", check.Status);
        Assert.True(check.BlocksProduction);
    }

    [Fact]
    public void Development_self_hosted_mode_still_requires_production_shape()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            SafeProductionSettings(new Dictionary<string, string?>()),
            new TestHostEnvironment("Development"));

        Assert.True(report.ProductionShapeRequired);
        Assert.True(report.ReadyForProductionStyleDeployment);
    }

    [Fact]
    public void Development_backend_only_compatibility_mode_does_not_force_production_shape()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            Configuration(new Dictionary<string, string?>
            {
                ["Platform:Mode"] = PlatformModes.BackendOnly,
                ["Database:Provider"] = "Sqlite"
            }),
            new TestHostEnvironment("Development"));

        Assert.False(report.ProductionShapeRequired);
        Assert.True(report.ReadyForProductionStyleDeployment);
    }

    [Fact]
    public void Development_local_demo_does_not_block_startup()
    {
        var report = ProductionEnvironmentReadinessValidator.GetReport(
            Configuration(new Dictionary<string, string?>
            {
                ["Platform:Mode"] = PlatformModes.LocalDemo,
                ["Database:Provider"] = "Sqlite",
                ["Bootstrap:SeedDemoData"] = "true",
                ["FeatureFlags:DemoExperience"] = "true",
                ["VITE_DEMO_FALLBACK"] = "true"
            }),
            new TestHostEnvironment("Development"));

        Assert.False(report.ProductionShapeRequired);
        Assert.True(report.ReadyForProductionStyleDeployment);
    }

    private static IConfiguration SafeProductionSettings(Dictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Platform:Mode"] = PlatformModes.SelfHosted,
            ["Database:Provider"] = "Postgres",
            ["ConnectionStrings:Scout"] = "Host=postgres.internal;Port=5432;Database=scout_context;Username=scout;Password=not-real",
            ["Bootstrap:SeedDemoData"] = "false",
            ["FeatureFlags:DemoExperience"] = "false",
            ["DataProtection:RequirePersistentKeys"] = "true",
            ["DataProtection:KeyRingPath"] = "C:\\scout-data-protection-keys",
            ["Auth:SigningKey"] = new string('a', 64),
            ["Auth:MinimumSigningKeyLength"] = "48",
            ["Auth:RequireSecureSigningKey"] = "true",
            ["Platform:EnableOpenApi"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://app.example.invalid",
            ["SecurityHeaders:Enabled"] = "true",
            ["SecurityHeaders:ContentSecurityPolicy"] = "default-src 'none'; frame-ancestors 'none'",
            ["VITE_DEMO_FALLBACK"] = "false"
        };

        foreach (var pair in overrides)
        {
            settings[pair.Key] = pair.Value;
        }

        return Configuration(settings);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "KynticAI.Scout.UnitTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
