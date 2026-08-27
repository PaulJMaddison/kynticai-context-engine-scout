using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KynticAI.Scout.IntegrationTests;

/// <summary>
/// Regression proof for WP-018: hosted ASP.NET Data Protection persistence must survive a
/// restart/redeploy, otherwise previously protected connector credentials become unreadable.
///
/// Configuring a key-ring path is not enough; the hosting platform must actually persist that
/// path. This proof stores a connector credential through IConnectorCredentialStore, disposes
/// the first DI container (simulating a shutdown), then builds a fresh container against the same
/// key-ring directory and persistent SQLite store (simulating a restart/redeploy) and resolves
/// the same secret. It never prints or exports key material; it only asserts the key ring was
/// written to disk and that the round-trip decrypts identically.
/// </summary>
public sealed class DataProtectionPersistenceProofTests
{
    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 08, 27, 12, 0, 0, DateTimeKind.Utc);
    }

    private static string NewKeyRingDirectory() =>
        Path.Combine(Path.GetTempPath(), $"scout-dp-keys-{Guid.NewGuid():N}");

    private static string NewDatabase() =>
        Path.Combine(Path.GetTempPath(), $"scout-dp-store-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildProvider(string keyRingPath, string dbPath)
    {
        var services = new ServiceCollection();
        services
            .AddDataProtection()
            .SetApplicationName("KynticAIScout")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddDbContext<ScoutDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IScoutDbContext>(provider => provider.GetRequiredService<ScoutDbContext>());
        services.AddSingleton<IClock>(new TestClock());
        services.AddScoped<IConnectorCredentialStore, ProtectedConnectorCredentialStore>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ProtectedConnectorCredential_SurvivesRestartRedeploy_WithPersistedKeyRing()
    {
        var keyRingPath = NewKeyRingDirectory();
        var dbPath = NewDatabase();
        Directory.CreateDirectory(keyRingPath);

        string tenantId;
        Guid dataSourceId;
        string apiKey;

        // First "process": persist a connector credential and write keys to the durable path.
        using (var provider = BuildProvider(keyRingPath, dbPath))
        {
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ScoutDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            tenantId = Guid.NewGuid().ToString("D");
            var tenant = Guid.Parse(tenantId);
            var tenantRow = Tenant.Create("dp-proof", "Data Protection Proof Tenant", DateTime.UtcNow);
            tenantId = tenantRow.Id.ToString("D");
            tenant = tenantRow.Id;
            dbContext.Tenants.Add(tenantRow);
            await dbContext.SaveChangesAsync();

            var dataSource = DataSource.Create(tenant, "CRM API", "test", DataSourceKind.Crm, """{"connectorType":"restApi"}""", DateTime.UtcNow);
            dbContext.DataSources.Add(dataSource);
            await dbContext.SaveChangesAsync();
            dataSourceId = dataSource.Id;

            var store = scope.ServiceProvider.GetRequiredService<IConnectorCredentialStore>();
            var refs = await store.PersistCredentialsAsync(
                tenant,
                dataSourceId,
                "restApi",
                new JsonObject { ["apiKey"] = "sup3r-s3cr3t-value" },
                CancellationToken.None);
            apiKey = refs["apiKey"]!.GetValue<string>();
            Assert.StartsWith("secret://", apiKey, StringComparison.Ordinal);
        }

        // The key ring directory must now contain persisted key material (check presence only).
        var keyFiles = Directory.Exists(keyRingPath)
            ? Directory.GetFiles(keyRingPath, "*.xml", SearchOption.AllDirectories)
            : [];
        Assert.NotEmpty(keyFiles);

        // Second "process": same durable key ring and persistent store (restart/redeploy).
        using (var provider = BuildProvider(keyRingPath, dbPath))
        {
            await using var scope = provider.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IConnectorCredentialStore>();
            var resolved = await store.ResolveConfigurationSecretsAsync(
                Guid.Parse(tenantId),
                new JsonObject
                {
                    ["connectorType"] = "restApi",
                    ["credentials"] = new JsonObject { ["apiKey"] = apiKey }
                },
                CancellationToken.None);

            var restored = resolved["credentials"]!["apiKey"]!.GetValue<string>();
            Assert.Equal("sup3r-s3cr3t-value", restored);
        }

        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; a locked handle should not mask the proof result.
        }
        try
        {
            if (Directory.Exists(keyRingPath))
            {
                Directory.Delete(keyRingPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }
}
