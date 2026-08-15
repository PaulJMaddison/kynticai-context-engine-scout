using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace KynticAI.Scout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddScoutApplication(this IServiceCollection services)
    {
        services.AddScoped<IScoutService, ScoutService>();
        services.AddScoped<IScoutUpgradeCompatibilityService, ScoutUpgradeCompatibilityService>();
        services.AddScoped<BasicRelationshipEngine>();
        services.AddScoped<EnterpriseRelationshipEngineHandoff>();
        services.AddScoped<INextActionIntelligenceService, NextActionIntelligenceService>();
        services.AddScoped<IBlueprintImportService, BlueprintImportService>();
        services.AddScoped<IBillingPlanCatalog, BillingPlanCatalog>();
        services.AddScoped<IUsageMeteringService, UsageMeteringService>();
        services.AddScoped<IBillingEnforcementService, BillingEnforcementService>();
        services.AddScoped<IBillingProviderGateway, NoopBillingProviderGateway>();
        services.AddValidatorsFromAssemblyContaining<UpsertDataSourceInputValidator>();
        return services;
    }
}
