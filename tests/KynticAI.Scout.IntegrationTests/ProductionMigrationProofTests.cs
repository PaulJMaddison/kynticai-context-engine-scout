using KynticAI.Scout.Infrastructure.Configuration;
using KynticAI.Scout.Infrastructure.Persistence;
using KynticAI.Scout.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KynticAI.Scout.IntegrationTests;

/// <summary>
/// Regression coverage for the production migration command.
///
/// Scout ships a single-store production data plane (one Scout PostgreSQL database, no
/// CustomerOps dependency). The explicit `migrate`/`bootstrap` command must run database
/// migration/bootstrap even though the normal production startup setting
/// <c>Bootstrap:ApplyMigrationsOnStartup=false</c> disables automatic migration on boot.
/// Otherwise a Render <c>preDeployCommand</c> would skip schema creation and fail later
/// when the connector catalogue is seeded against a database whose schema does not exist.
/// </summary>
public sealed class ProductionMigrationProofTests
{
    private static ServiceProvider BuildProvider(string sqliteConnectionString)
    {
        var services = new ServiceCollection();

        // Only the single Scout store is registered. There is deliberately NO
        // CustomerOpsDbContext, proving bootstrap does not require it. A temp-file SQLite
        // database is used so every EF connection observes the same persistent schema.
        services.AddDbContext<ScoutDbContext>(options =>
            options.UseSqlite(sqliteConnectionString));
        services.AddScoped<KynticAI.Scout.Application.Abstractions.IScoutDbContext>(provider =>
            provider.GetRequiredService<ScoutDbContext>());

        return services.BuildServiceProvider();
    }

    private static string NewTempDatabase() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"scout-migrate-test-{Guid.NewGuid():N}.db")}";

    [Fact]
    public async Task ExplicitMigrateBootstrap_CreatesSingleScoutSchemaAndSeedsCatalogue_WithoutCustomerOps()
    {
        var connectionString = NewTempDatabase();

        // Explicit migrate/bootstrap forces ApplyMigrationsOnStartup = true even when the
        // production setting is false (BootstrapCommandResolver.Resolve).
        var resolved = BootstrapCommandResolver.Resolve(
            new BootstrapOptions { ApplyMigrationsOnStartup = false, SeedDemoData = false },
            explicitMigrationCommand: true,
            explicitSeedDemoCommand: false);
        Assert.True(resolved.ApplyMigrationsOnStartup);

        await using var provider = BuildProvider(connectionString);
        await ApplicationBootstrapper.InitializeAsync(
            provider,
            resolved,
            new ConnectorBootstrapOptions());

        // InitializeAsync completed without throwing, which proves the full Scout schema was
        // created (connector-catalogue seeding requires it). Querying a core Scout DbSet via
        // EF additionally proves the table exists rather than relying on raw SQLite metadata.
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        await dbContext.Tenants.CountAsync();
        var catalogueCount = await dbContext.ConnectorCatalogueEntries.CountAsync();
        Assert.True(catalogueCount > 0, "Connector catalogue should be seeded after schema creation.");
    }

    [Fact]
    public async Task OrdinaryStartup_WithMigrationsDisabled_DoesNotUnexpectedlyMigrate()
    {
        var connectionString = NewTempDatabase();

        var resolved = BootstrapCommandResolver.Resolve(
            new BootstrapOptions { ApplyMigrationsOnStartup = false, SeedDemoData = false },
            explicitMigrationCommand: false,
            explicitSeedDemoCommand: false);
        Assert.False(resolved.ApplyMigrationsOnStartup, "Ordinary startup must preserve the disabled migration setting.");

        await using var provider = BuildProvider(connectionString);

        // Because startup migrations are disabled and no explicit migrate command ran, the
        // Scout schema does not exist; connector-catalogue seeding therefore fails instead of
        // silently triggering an implicit migration. This proves startup does not migrate on
        // its own and that the deploy-time migrate command is the required path.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            ApplicationBootstrapper.InitializeAsync(
                provider,
                resolved,
                new ConnectorBootstrapOptions()));

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => dbContext.Tenants.CountAsync());
    }
}
