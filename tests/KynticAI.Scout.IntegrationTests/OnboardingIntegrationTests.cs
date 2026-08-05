using System.Net;
using System.Net.Http.Json;
using KynticAI.Scout.Api.Onboarding;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Configuration;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KynticAI.Scout.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class OnboardingIntegrationTests
{
    [Fact]
    public async Task SubmitOnboarding_ProvisionsTenantScopedStarterWorkspace_WithoutCrossTenantLeakage()
    {
        await using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();

        var acmeResponse = await client.PostAsJsonAsync("/api/onboarding", CreateInput(
            tenantSlug: "acme-revenue",
            organisationName: "Acme Revenue Systems",
            adminEmail: "dana@acme.example",
            sourceSystems: ["Salesforce CRM", "Zendesk Support"],
            dataCategories: ["CRM", "Support"],
            aiUseCases: ["Sales copilot"]));
        var betaResponse = await client.PostAsJsonAsync("/api/onboarding", CreateInput(
            tenantSlug: "beta-ai",
            organisationName: "Beta AI Works",
            adminEmail: "alex@beta.example",
            sourceSystems: ["Stripe Billing", "Snowflake Warehouse"],
            dataCategories: ["Billing", "Warehouse"],
            aiUseCases: ["Executive account briefs"]));

        Assert.Equal(HttpStatusCode.Created, acmeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, betaResponse.StatusCode);

        var acmeResult = await acmeResponse.Content.ReadFromJsonAsync<OnboardingResult>();
        var betaResult = await betaResponse.Content.ReadFromJsonAsync<OnboardingResult>();
        Assert.NotNull(acmeResult);
        Assert.NotNull(betaResult);
        Assert.NotEqual(acmeResult!.TenantId, betaResult!.TenantId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();

        var acmeTenant = await dbContext.Tenants.SingleAsync(x => x.Slug == "acme-revenue");
        var betaTenant = await dbContext.Tenants.SingleAsync(x => x.Slug == "beta-ai");
        var acmeSources = await dbContext.DataSources
            .Where(x => x.TenantId == acmeTenant.Id)
            .Select(x => x.Name)
            .ToListAsync();
        var betaSources = await dbContext.DataSources
            .Where(x => x.TenantId == betaTenant.Id)
            .Select(x => x.Name)
            .ToListAsync();

        Assert.Contains(acmeSources, name => name.Contains("Salesforce CRM", StringComparison.Ordinal));
        Assert.DoesNotContain(acmeSources, name => name.Contains("Stripe Billing", StringComparison.Ordinal));
        Assert.Contains(betaSources, name => name.Contains("Stripe Billing", StringComparison.Ordinal));
        Assert.DoesNotContain(betaSources, name => name.Contains("Salesforce CRM", StringComparison.Ordinal));

        Assert.Equal(1, await dbContext.Workspaces.CountAsync(x => x.TenantId == acmeTenant.Id));
        Assert.Equal(1, await dbContext.Workspaces.CountAsync(x => x.TenantId == betaTenant.Id));
        Assert.Equal(1, await dbContext.OperatorAccounts.CountAsync(x => x.TenantId == acmeTenant.Id && x.Role == OperatorRole.TenantAdmin));
        Assert.Equal(1, await dbContext.OperatorAccounts.CountAsync(x => x.TenantId == betaTenant.Id && x.Role == OperatorRole.TenantAdmin));
        Assert.True(await dbContext.SemanticAttributeDefinitions.AnyAsync(x => x.TenantId == acmeTenant.Id && x.Key == "supportRisk"));
        Assert.False(await dbContext.SemanticAttributeDefinitions.AnyAsync(x => x.TenantId == betaTenant.Id && x.Key == "supportRisk"));
        Assert.Equal(2, await dbContext.OnboardingApplications.CountAsync());
        Assert.Equal(2, await dbContext.AuditEvents.CountAsync(x => x.Action == "onboarding.provisioned"));
    }

    [Fact]
    public void OnboardingAccessGuard_DeniesProductionByDefault()
    {
        var guard = new OnboardingAccessGuard(
            Options.Create(new PlatformOptions { Mode = PlatformModes.SaaS }),
            Options.Create(new FeatureFlagOptions
            {
                AnonymousOnboarding = true,
                AllowProductionOnboarding = false
            }),
            new TestHostEnvironment("Production"),
            NullLogger<OnboardingAccessGuard>.Instance);

        Assert.False(guard.IsAnonymousOnboardingAllowed());
    }

    private static SubmitOnboardingInput CreateInput(
        string tenantSlug,
        string organisationName,
        string adminEmail,
        IReadOnlyList<string> sourceSystems,
        IReadOnlyList<string> dataCategories,
        IReadOnlyList<string> aiUseCases)
        => new(
            OrganisationName: organisationName,
            TenantSlug: tenantSlug,
            PrimaryWorkspaceName: "Revenue workspace",
            AdminDisplayName: "Demo Admin",
            AdminEmail: adminEmail,
            AdminPassword: "DemoPassword123!",
            IntendedUseCase: "Generate trusted account briefs from existing business systems.",
            SourceSystems: sourceSystems,
            DataCategories: dataCategories,
            AiUseCases: aiUseCases,
            PiiSensitivityLevel: "moderate",
            PreferredDeploymentMode: "local-demo");

    private sealed class OnboardingWebApplicationFactory(
        string environmentName = "Development",
        string platformMode = "BackendOnly",
        bool anonymousOnboarding = true,
        bool allowProductionOnboarding = false) : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot databaseRoot = new();
        private readonly string databaseName = $"onboarding-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Platform:Mode"] = platformMode,
                    ["Platform:EnableRest"] = "true",
                    ["Platform:EnableGraphQl"] = "true",
                    ["Platform:EnableOpenApi"] = "false",
                    ["FeatureFlags:AnonymousOnboarding"] = anonymousOnboarding.ToString(),
                    ["FeatureFlags:AllowProductionOnboarding"] = allowProductionOnboarding.ToString(),
                    ["Bootstrap:ApplyMigrationsOnStartup"] = "false",
                    ["Bootstrap:SeedDemoData"] = "false",
                    ["Auth:Issuer"] = "KynticAI.Scout.Tests",
                    ["Auth:Audience"] = "KynticAI.Scout.Tests",
                    ["Auth:SigningKey"] = "scout-tests-signing-key-1234567890-extra-production-safe-length",
                    ["Auth:MinimumSigningKeyLength"] = "32",
                    ["Auth:RequireSecureSigningKey"] = environmentName == "Production" ? "false" : "true",
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

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "KynticAI.Scout.IntegrationTests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
