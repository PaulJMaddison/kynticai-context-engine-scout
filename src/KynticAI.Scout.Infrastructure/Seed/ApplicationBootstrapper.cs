using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Infrastructure.Configuration;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KynticAI.Scout.Infrastructure.Seed;

public static class ApplicationBootstrapper
{
    public static async Task InitializeAsync(
        IServiceProvider serviceProvider,
        BootstrapOptions bootstrapOptions,
        ConnectorBootstrapOptions connectorBootstrapOptions,
        CancellationToken cancellationToken = default)
    {
        if (bootstrapOptions.ApplyMigrationsOnStartup)
        {
            await EnsureDatabasesReadyAsync(serviceProvider, cancellationToken);
        }

        await EnsureConnectorCatalogueSeededAsync(serviceProvider, cancellationToken);

        if (bootstrapOptions.SeedDemoData)
        {
            await DemoDataSeeder.SeedAsync(serviceProvider, cancellationToken);
        }

        if (connectorBootstrapOptions.Enabled && connectorBootstrapOptions.Definitions.Count > 0)
        {
            await BootstrapConnectorsAsync(serviceProvider, connectorBootstrapOptions.Definitions, cancellationToken);
        }
    }

    private static async Task EnsureConnectorCatalogueSeededAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var contextDbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        await ConnectorCatalogueSeeder.SeedAsync(contextDbContext, DateTime.UtcNow, cancellationToken);
    }

    private static async Task EnsureDatabasesReadyAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var contextDbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
        var customerOpsDbContext = scope.ServiceProvider.GetService<CustomerOpsDbContext>();

        await EnsureDatabaseReadyAsync(contextDbContext, cancellationToken);
        if (customerOpsDbContext is not null)
        {
            await EnsureDatabaseReadyAsync(customerOpsDbContext, cancellationToken);
        }
    }

    private static async Task EnsureDatabaseReadyAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        if (string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
            return;
        }

        var created = await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        if (!created && string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            await EnsureMissingSqliteObjectsAsync(dbContext, cancellationToken);
        }
    }

    private static async Task EnsureMissingSqliteObjectsAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var createScript = dbContext.Database.GenerateCreateScript();
        var statements = createScript
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static statement => !string.IsNullOrWhiteSpace(statement));

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException exception) when (
                exception.SqliteErrorCode == 1
                && (exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                    || exception.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
            {
                // Local SQLite demos may be upgraded from older EnsureCreated databases.
                // Ignore existing objects and create any newly introduced tables/indexes.
            }
        }
    }

    private static async Task BootstrapConnectorsAsync(
        IServiceProvider serviceProvider,
        IReadOnlyList<ConnectorBootstrapDefinition> definitions,
        CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IScoutService>();

        foreach (var definition in definitions)
        {
            var existingDataSource = (await service.GetDataSourcesAsync(definition.TenantSlug, cancellationToken))
                .FirstOrDefault(candidate => string.Equals(candidate.Name, definition.Name, StringComparison.OrdinalIgnoreCase));

            await service.RegisterConnectorAsync(
                new RegisterConnectorInput(
                    existingDataSource?.Id,
                    definition.TenantSlug,
                    definition.Name,
                    definition.Description,
                    definition.Kind,
                    definition.ConnectorType,
                    definition.ConfigurationJson,
                    definition.CredentialsJson),
                cancellationToken);
        }
    }
}
