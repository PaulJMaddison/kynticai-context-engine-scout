using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Infrastructure.Auth;
using KynticAI.Scout.Infrastructure.AI;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Extensions;
using KynticAI.Scout.Infrastructure.Jobs;
using KynticAI.Scout.Infrastructure.Onboarding;
using KynticAI.Scout.Infrastructure.Persistence;
using KynticAI.Scout.Infrastructure.Selectors;
using KynticAI.Scout.Infrastructure.Services;
using KynticAI.Scout.Infrastructure.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KynticAI.Scout.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScoutInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment? environment = null)
    {
        var platformOptions = configuration.GetSection(PlatformOptions.SectionName).Get<PlatformOptions>() ?? new PlatformOptions();
        var dataProtectionOptions = configuration.GetSection(DataProtectionKeyOptions.SectionName).Get<DataProtectionKeyOptions>() ?? new DataProtectionKeyOptions();
        var hostedMode = (environment?.IsProduction() ?? false)
            || string.Equals(platformOptions.Mode, PlatformModes.SaaS, StringComparison.OrdinalIgnoreCase);
        if (hostedMode
            && dataProtectionOptions.RequirePersistentKeys
            && string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
        {
            throw new InvalidOperationException("DataProtection:KeyRingPath must be set to persistent storage before running in Production or SaaS mode with connector credential protection.");
        }

        services.AddDbContext<ScoutDbContext>(options =>
            DatabaseProviderConfigurator.ConfigureScout(options, configuration));

        services.AddDbContext<CustomerOpsDbContext>(options =>
            DatabaseProviderConfigurator.ConfigureCustomerOps(options, configuration));

        services.AddScoped<IScoutDbContext>(provider => provider.GetRequiredService<ScoutDbContext>());
        services.AddScoped<ICustomerOpsDbContext>(provider => provider.GetRequiredService<CustomerOpsDbContext>());
        services.AddScoped<IPlatformRuntimeOptions, PlatformRuntimeOptions>();
        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName(string.IsNullOrWhiteSpace(dataProtectionOptions.ApplicationName)
                ? "KynticAIScout"
                : dataProtectionOptions.ApplicationName);
        if (!string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionOptions.KeyRingPath));
        }

        services.AddHttpClient("scout-connectors");
        services.AddHttpClient<IControlPlaneEntitlementClient, CloudControlPlaneEntitlementClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ControlPlaneOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl)
                && Uri.TryCreate(options.BaseUrl.Trim(), UriKind.Absolute, out var baseUri)
                && (baseUri.Scheme == Uri.UriSchemeHttps || baseUri.Scheme == Uri.UriSchemeHttp))
            {
                client.BaseAddress = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                    ? baseUri
                    : new Uri($"{baseUri.AbsoluteUri}/");
            }

            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 60));
        });
        services.AddHttpContextAccessor();
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<PlatformOptions>(configuration.GetSection(PlatformOptions.SectionName));
        services.Configure<FeatureFlagOptions>(configuration.GetSection(FeatureFlagOptions.SectionName));
        services.Configure<SaaSOptions>(configuration.GetSection(SaaSOptions.SectionName));
        services.Configure<ControlPlaneOptions>(configuration.GetSection(ControlPlaneOptions.SectionName));
        services.Configure<LicenceOptions>(configuration.GetSection(LicenceOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        services.Configure<DataProtectionKeyOptions>(configuration.GetSection(DataProtectionKeyOptions.SectionName));
        services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.Configure<ConnectorBootstrapOptions>(configuration.GetSection(ConnectorBootstrapOptions.SectionName));
        services.Configure<StorageAdapterOptions>(configuration.GetSection(StorageAdapterOptions.SectionName));
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PasswordHashingService>();
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<MachineClientAuthenticationService>();
        services.AddScoped<ApiClientKeyService>();
        services.AddScoped<WebhookSigningSecretService>();
        services.AddScoped<ICurrentActorService, CurrentActorService>();
        services.AddSingleton<IBackgroundJobMonitor, InMemoryBackgroundJobMonitor>();
        services.AddSingleton<BackgroundJobMetrics>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<BackgroundJobMetrics>());
        services.AddScoped<ContextRecomputeProcessor>();
        services.AddScoped<ISalesSupportAgentService, SalesSupportAgentService>();
        services.AddScoped<IStructuredLlmClient, MockStructuredLlmClient>();
        services.AddScoped<IStructuredLlmClientRegistry, StructuredLlmClientRegistry>();

        // Keep the existing selector implementation as the derivation engine, but make source
        // capture a separate decorator so source truth is durably retained before Scout's
        // derived ContextFact/Snapshot state becomes the only visible result of a connector read.
        services.AddScoped<SelectorExecutionEngine>();
        services.AddScoped<LocalSourceCaptureJournal>();
        services.AddScoped<ISelectorExecutionEngine, UpgradeCapturingSelectorExecutionEngine>();

        services.AddScoped<IOnboardingService, SaasOnboardingService>();
        services.AddScoped<IScheduledRecomputeDispatcher, ScheduledRecomputeDispatcher>();
        services.AddScoped<IConnectorPlugin, MockConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, RestApiConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, SqlConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, CsvUploadConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, MockCrmConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, MockBillingConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, MockSupportConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, InMemoryInventoryConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, TemplateConnectorPlugin>();
        services.AddScoped<IConnectorRegistry, ConnectorRegistry>();
        services.AddScoped<IConnectorCredentialStore, ProtectedConnectorCredentialStore>();
        services.AddSingleton<ILicenceKeyGenerator, StaticLicenceKeyGenerator>();
        services.AddSingleton<ILicenceValidator, OfflineLicenceValidator>();
        services.AddScoped<ILicenceService, LocalLicenceService>();
        services.AddEnterpriseExtensionDefaults();
        services.AddSingleton<ContextRecomputeQueue>();
        services.AddSingleton<IContextRecomputeQueue>(provider => provider.GetRequiredService<ContextRecomputeQueue>());
        services.AddHostedService<ContextRecomputeWorker>();
        services.AddHostedService<ScheduledRecomputeWorker>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
