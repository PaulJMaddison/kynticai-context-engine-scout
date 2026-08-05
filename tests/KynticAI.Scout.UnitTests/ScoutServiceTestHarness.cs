using KynticAI.Scout.Application;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Infrastructure.AI;
using KynticAI.Scout.Infrastructure.Auth;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Jobs;
using KynticAI.Scout.Infrastructure.Persistence;
using KynticAI.Scout.Infrastructure.Selectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KynticAI.Scout.UnitTests;

internal sealed class ScoutServiceTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider serviceProvider;
    private readonly AsyncServiceScope scope;

    private ScoutServiceTestHarness(
        ServiceProvider serviceProvider,
        AsyncServiceScope scope,
        TestClock clock)
    {
        this.serviceProvider = serviceProvider;
        this.scope = scope;
        Clock = clock;
        DbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        CustomerOpsDbContext = scope.ServiceProvider.GetRequiredService<CustomerOpsDbContext>();
        Service = scope.ServiceProvider.GetRequiredService<IScoutService>();
    }

    public TestClock Clock { get; }

    public ScoutDbContext DbContext { get; }

    public CustomerOpsDbContext CustomerOpsDbContext { get; }

    public IScoutService Service { get; }

    public static async Task<ScoutServiceTestHarness> CreateAsync(
        string mode = "BackendOnly",
        params string[] featureFlags)
    {
        var clock = new TestClock(new DateTime(2026, 05, 09, 12, 00, 00, DateTimeKind.Utc));
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ScoutDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddDbContext<CustomerOpsDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSingleton<IClock>(clock);
        services.AddDataProtection();
        services.AddHttpClient("scout-connectors");
        services.AddScoped<IScoutDbContext>(provider => provider.GetRequiredService<ScoutDbContext>());
        services.AddScoped<ICustomerOpsDbContext>(provider => provider.GetRequiredService<CustomerOpsDbContext>());
        services.AddScoped<IPlatformRuntimeOptions>(_ => new TestPlatformRuntimeOptions(mode, featureFlags));
        services.AddScoped<ISelectorExecutionEngine, SelectorExecutionEngine>();
        services.AddScoped<IScheduledRecomputeDispatcher, ScheduledRecomputeDispatcher>();
        services.AddScoped<IStructuredLlmClient, MockStructuredLlmClient>();
        services.AddScoped<IStructuredLlmClientRegistry, StructuredLlmClientRegistry>();
        services.AddScoped<ISalesSupportAgentService, SalesSupportAgentService>();
        services.AddScoped<ContextRecomputeProcessor>();
        services.AddScoped<IConnectorPlugin, MockConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, RestApiConnectorPlugin>();
        services.AddScoped<IConnectorPlugin, SqlConnectorPlugin>();
        services.AddScoped<IConnectorRegistry, ConnectorRegistry>();
        services.AddScoped<IConnectorCredentialStore, ProtectedConnectorCredentialStore>();
        services.AddSingleton<IBackgroundJobMonitor, InMemoryBackgroundJobMonitor>();
        services.AddSingleton<ContextRecomputeQueue>();
        services.AddSingleton<IContextRecomputeQueue>(provider => provider.GetRequiredService<ContextRecomputeQueue>());
        services.AddSingleton<ICurrentActorService>(new TestCurrentActorService(ActorContext.System()));
        services.AddSingleton<IOptions<LlmOptions>>(Options.Create(new LlmOptions
        {
            DefaultProvider = "mock",
            DefaultModel = "gpt-5.5",
            MaxAttempts = 2,
            LowConfidenceThreshold = 0.75m,
            MinimumStrongFacts = 3
        }));
        services.AddScoutApplication();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        var harness = new ScoutServiceTestHarness(provider, scope, clock);
        harness.DbContext.Tenants.Add(Tenant.Create("demo", "Demo Tenant", clock.UtcNow));
        await harness.DbContext.SaveChangesAsync();
        return harness;
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    public sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class TestCurrentActorService(ActorContext actorContext) : ICurrentActorService
    {
        public ActorContext GetCurrentActor() => actorContext;
    }

    private sealed class TestPlatformRuntimeOptions(string mode, params string[] featureFlags) : IPlatformRuntimeOptions
    {
        public string Mode => mode;

        public IReadOnlyList<string> EnabledFeatureFlags => featureFlags;
    }
}
