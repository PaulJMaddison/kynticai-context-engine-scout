using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace KynticAI.Scout.UnitTests;

/// <summary>
/// Real cross-repo fixture proof. Runs only when KYNTIC_RUN_EXTERNAL_DOTNET_TESTS=1 because it
/// needs the disposable local PostgreSQL container and invokes the actual
/// KynticAI.Scout.UpgradeExport tool. Never runs in ordinary unit-test runs; never touches
/// KynticAI Cloud or any non-disposable service.
/// </summary>
[Trait("Category", "External")]
public sealed class FortressCrossRepoFixtureTests
{
    private const string AdminDatabase = "scout_fixture";

    private static bool ExternalEnabled
        => string.Equals(
            Environment.GetEnvironmentVariable("KYNTIC_RUN_EXTERNAL_DOTNET_TESTS"),
            "1",
            StringComparison.Ordinal);

    private static string Host => Environment.GetEnvironmentVariable("SCOUT_FIXTURE_PG_HOST") ?? "localhost";
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("SCOUT_FIXTURE_PG_PORT"), out var value) ? value : 55432;
    private static string User => Environment.GetEnvironmentVariable("SCOUT_FIXTURE_PG_USER") ?? "scout";
    private static string Password => Environment.GetEnvironmentVariable("SCOUT_FIXTURE_PG_PASSWORD") ?? "scoutpw_2026";

    private static string AdminConnectionString
        => $"Host={Host};Port={Port};Database={AdminDatabase};Username={User};Password={Password}";

    private static string DatabaseConnectionString(string database)
        => $"Host={Host};Port={Port};Database={database};Username={User};Password={Password}";

    private static string ArtifactDirectory
        => Environment.GetEnvironmentVariable("SCOUT_FIXTURE_ARTIFACT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "scout-fortress-tiny-proof");

    [Fact]
    public async Task RealPostgresFixture_ExportsOnlyLatestCompletedGeneration()
    {
        if (!ExternalEnabled)
        {
            return;
        }

        await using var fixture = await FixtureDb.CreateAsync("proof_green");
        var (mainTenant, mainInstallation) = await fixture.AddCsvInstallationAsync("proof-green", CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("b", "Bob", "2026-08-15T10:05:00Z")));
        var (_, emptyInstallation) = await fixture.AddCsvInstallationAsync("proof-green", CsvRows());

        var coordinator = fixture.CreateCoordinator(new CsvFullSourceCaptureConnector());

        // Generation 1: {A, B} completes for main; the empty source completes proven-empty.
        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 2, CancellationToken.None);

        // Generation 2: the source now contains only {A}; Bob was deleted between generations.
        await fixture.ChangeCsvRowsAsync(mainInstallation, CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z")));
        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 2, CancellationToken.None);

        // Generation 3 is deliberately left IN-FLIGHT (only page 1 of a paged enumeration).
        await fixture.ChangeCsvRowsAsync(mainInstallation, CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("c", "Carol", "2026-08-15T10:10:00Z")));
        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 1, CancellationToken.None);

        var mainCheckpoint = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == mainInstallation.Id);
        Assert.Equal(2, mainCheckpoint.Generation);
        Assert.False(string.IsNullOrWhiteSpace(mainCheckpoint.ContinuationToken));

        var emptyCheckpoint = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == emptyInstallation.Id);
        Assert.True(emptyCheckpoint.Generation > 0);
        Assert.NotNull(emptyCheckpoint.LastFullSourceCompletedAtUtc);

        var outputPath = Path.Combine(ArtifactDirectory, "green.scout-source.jsonl");
        var result = await RunExporterAsync(fixture.ConnectionString, "proof-green", outputPath, overwrite: true);

        Assert.True(result.ExitCode == 0, $"Exporter failed:{Environment.NewLine}{result.Stderr}");

        Assert.True(File.Exists(outputPath), "Green JSONL was not written.");
        var manifestPath = outputPath + ".manifest.json";
        Assert.True(File.Exists(manifestPath), "Green manifest was not written.");

        var lines = (await File.ReadAllLinesAsync(outputPath)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        Assert.Single(lines);
        using var exportedRow = JsonDocument.Parse(lines[0]);
        Assert.Equal(mainInstallation.Id, exportedRow.RootElement.GetProperty("ConnectorInstallationId").GetGuid());
        Assert.Equal(2, exportedRow.RootElement.GetProperty("CaptureGeneration").GetInt64());
        Assert.Equal("contact", exportedRow.RootElement.GetProperty("SourceObjectType").GetString());
        Assert.Equal("a", exportedRow.RootElement.GetProperty("SourceRecordId").GetString());
        Assert.Equal($"kyntic-connector:{mainInstallation.Id:D}", exportedRow.RootElement.GetProperty("SourceNamespace").GetString());
        Assert.Equal(mainTenant.Id, exportedRow.RootElement.GetProperty("TenantId").GetGuid());
        Assert.DoesNotContain(lines[0], "bob", StringComparison.OrdinalIgnoreCase);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal("kyntic-scout-source-journal-export.v2", manifest.RootElement.GetProperty("Contract").GetString());
        Assert.Equal(mainTenant.Id, manifest.RootElement.GetProperty("TenantId").GetGuid());
        Assert.Equal("proof-green", manifest.RootElement.GetProperty("TenantSlug").GetString());
        Assert.Equal(1, manifest.RootElement.GetProperty("Rows").GetInt64());
        Assert.Equal("exact-text.v1", manifest.RootElement.GetProperty("PayloadStorageContract").GetString());
        Assert.Equal("generation-membership.v1", manifest.RootElement.GetProperty("GenerationMembershipContract").GetString());
        Assert.False(manifest.RootElement.GetProperty("ContainsCredentialValues").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("ContainsProtectedCredentialReferences").GetBoolean());
        Assert.True(manifest.RootElement.GetProperty("ContainsExactCustomerPayloads").GetBoolean());

        var selections = manifest.RootElement.GetProperty("ConnectorSelections").EnumerateArray().ToArray();
        Assert.Equal(2, selections.Length);
        var mainSelection = Assert.Single(selections, s => s.GetProperty("ConnectorInstallationId").GetGuid() == mainInstallation.Id);
        Assert.Equal(2, mainSelection.GetProperty("Generation").GetInt64());
        Assert.Equal(1, mainSelection.GetProperty("MemberCount").GetInt64());
        var emptySelection = Assert.Single(selections, s => s.GetProperty("ConnectorInstallationId").GetGuid() == emptyInstallation.Id);
        Assert.Equal(0, emptySelection.GetProperty("MemberCount").GetInt64());

        var actualSha256 = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(outputPath))).ToLowerInvariant();
        Assert.Equal(manifest.RootElement.GetProperty("JournalSha256").GetString(), actualSha256);

        Assert.DoesNotContain("scoutpw_2026", await File.ReadAllTextAsync(outputPath), StringComparison.Ordinal);
        Assert.DoesNotContain("scoutpw_2026", await File.ReadAllTextAsync(manifestPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealPostgresFixture_LegacyMembershipContract_IsRejectedByExporter()
    {
        if (!ExternalEnabled)
        {
            return;
        }

        await using var fixture = await FixtureDb.CreateAsync("proof_legacy");
        var (tenant, installation) = await fixture.AddCsvInstallationAsync("proof-legacy", CsvRows());
        var now = DateTime.SpecifyKind(new DateTime(2026, 8, 15, 12, 0, 0), DateTimeKind.Utc);
        var legacy = ConnectorCaptureCheckpoint.Create(
            tenant.Id,
            installation.Id,
            installation.DataSourceId,
            LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
            "1",
            LocalDataPlaneContracts.CoverageFullSource,
            LocalDataPlaneContracts.HistorySnapshotOnly,
            null,
            now,
            LocalDataPlaneContracts.PayloadStorageExactTextV1,
            LocalDataPlaneContracts.CurrentStateImmutableSnapshot,
            LocalDataPlaneContracts.GenerationMembershipUnknown);
        Assert.True(legacy.TryAcquireLease("legacy", TimeSpan.FromMinutes(5), now));
        legacy.ObserveCaptureSemantics("legacy", LocalDataPlaneContracts.HistorySnapshotOnly, null, now.AddSeconds(1), LocalDataPlaneContracts.CurrentStateImmutableSnapshot);
        legacy.Advance("legacy", null, "{}", 0, null, null, now.AddSeconds(2));
        legacy.CompleteFullSourceGeneration(
            "legacy",
            "{}",
            LocalDataPlaneContracts.HistorySnapshotOnly,
            now.AddSeconds(3),
            LocalDataPlaneContracts.PayloadStorageExactTextV1,
            LocalDataPlaneContracts.GenerationMembershipUnknown);
        legacy.ReleaseLease("legacy", now.AddSeconds(4));
        fixture.Scout.ConnectorCaptureCheckpoints.Add(legacy);
        await fixture.Scout.SaveChangesAsync();

        var outputPath = Path.Combine(ArtifactDirectory, "legacy-reject.scout-source.jsonl");
        var result = await RunExporterAsync(fixture.ConnectionString, "proof-legacy", outputPath, overwrite: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("generation-membership.v1", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath), "Legacy checkpoint must not produce an export file.");
    }

    [Fact]
    public async Task RealPostgresFixture_IncompleteInstallation_IsRejectedByExporter()
    {
        if (!ExternalEnabled)
        {
            return;
        }

        await using var fixture = await FixtureDb.CreateAsync("proof_incomplete");
        var (_, installation) = await fixture.AddCsvInstallationAsync("proof-incomplete", CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("b", "Bob", "2026-08-15T10:05:00Z")));
        var coordinator = fixture.CreateCoordinator(new CsvFullSourceCaptureConnector());

        // Only page 1 of generation 1 is captured; the generation is never completed.
        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 1, CancellationToken.None);
        var checkpoint = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == installation.Id);
        Assert.Null(checkpoint.LastFullSourceCompletedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(checkpoint.ContinuationToken));

        var outputPath = Path.Combine(ArtifactDirectory, "incomplete-reject.scout-source.jsonl");
        var result = await RunExporterAsync(fixture.ConnectionString, "proof-incomplete", outputPath, overwrite: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not have a completed", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath), "Incomplete capture must not produce an export file.");
    }

    [Fact]
    public async Task RealPostgresFixture_SafetyTampering_IsRejectedByExporter()
    {
        if (!ExternalEnabled)
        {
            return;
        }

        await using var fixture = await FixtureDb.CreateAsync("proof_tamper");
        var (_, installation) = await fixture.AddCsvInstallationAsync("proof-tamper", CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("b", "Bob", "2026-08-15T10:05:00Z")));
        var coordinator = fixture.CreateCoordinator(new CsvFullSourceCaptureConnector());
        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 2, CancellationToken.None);

        var memberA = await fixture.Scout.SourceCaptureGenerationMembers.SingleAsync(x => x.SourceRecordId == "a");
        var evidenceA = await fixture.Scout.SourceCapturePayloadEvidenceRecords
            .SingleAsync(x => x.SourceSystemEventId == memberA.SourceSystemEventId);
        var evidenceHashA = evidenceA.RawPayloadSha256;
        var evidencePayloadA = evidenceA.ExactPayloadText;
        var evidenceColumns = new
        {
            evidenceA.Id,
            evidenceA.TenantId,
            evidenceA.SourceSystemEventId,
            evidenceA.ConnectorInstallationId,
            evidenceA.StorageContract,
            evidenceA.CoverageScope,
            evidenceA.CreatedAtUtc,
            evidenceA.UpdatedAtUtc
        };

        var baseOutput = Path.Combine(ArtifactDirectory, "tamper-reject.scout-source.jsonl");

        // 1. Selected membership whose exact evidence is missing -> exporter refuses.
        await fixture.ExecuteSqlAsync("delete from source_capture_payload_evidence where \"Id\" = @id", new NpgsqlParameter("id", evidenceColumns.Id));
        var missingEvidence = await RunExporterAsync(fixture.ConnectionString, "proof-tamper", baseOutput, overwrite: true);
        Assert.NotEqual(0, missingEvidence.ExitCode);
        Assert.Contains("do not have exact customer-local payload evidence", missingEvidence.Stderr, StringComparison.OrdinalIgnoreCase);
        await fixture.ExecuteSqlAsync(
            """
            insert into source_capture_payload_evidence
                ("Id", "TenantId", "SourceSystemEventId", "ConnectorInstallationId", "StorageContract", "CoverageScope", "ExactPayloadText", "RawPayloadSha256", "CreatedAtUtc", "UpdatedAtUtc")
            values
                (@id, @tenant, @event, @connector, @storage, @coverage, @payload, @hash, @created, @updated)
            """,
            new NpgsqlParameter("id", evidenceColumns.Id),
            new NpgsqlParameter("tenant", evidenceColumns.TenantId),
            new NpgsqlParameter("event", evidenceColumns.SourceSystemEventId),
            new NpgsqlParameter("connector", evidenceColumns.ConnectorInstallationId),
            new NpgsqlParameter("storage", evidenceColumns.StorageContract),
            new NpgsqlParameter("coverage", evidenceColumns.CoverageScope),
            new NpgsqlParameter("payload", evidencePayloadA),
            new NpgsqlParameter("hash", evidenceHashA),
            new NpgsqlParameter("created", evidenceColumns.CreatedAtUtc),
            new NpgsqlParameter("updated", evidenceColumns.UpdatedAtUtc));

        // 2. Payload SHA contradiction -> exporter refuses.
        await fixture.ExecuteSqlAsync(
            "update source_capture_payload_evidence set \"RawPayloadSha256\" = @bogus where \"Id\" = @id",
            new NpgsqlParameter("bogus", new string('0', 64)),
            new NpgsqlParameter("id", evidenceColumns.Id));
        var shaContradiction = await RunExporterAsync(fixture.ConnectionString, "proof-tamper", baseOutput, overwrite: true);
        Assert.NotEqual(0, shaContradiction.ExitCode);
        Assert.Contains("hash mismatch", shaContradiction.Stderr, StringComparison.OrdinalIgnoreCase);
        await fixture.ExecuteSqlAsync(
            "update source_capture_payload_evidence set \"RawPayloadSha256\" = @hash where \"Id\" = @id",
            new NpgsqlParameter("hash", evidenceHashA),
            new NpgsqlParameter("id", evidenceColumns.Id));

        // 3. Membership count vs emitted row mismatch (membership points at another tenant's
        // event, which the tenant-scoped export join drops) -> exporter refuses.
        var foreignTenant = Tenant.Create("proof-foreign", "Proof Foreign", DateTime.UtcNow);
        var foreignWorkspace = Workspace.Create(foreignTenant.Id, "default", "Default", "Default", true, DateTime.UtcNow);
        fixture.Scout.AddRange(foreignTenant, foreignWorkspace);
        await fixture.Scout.SaveChangesAsync();
        var foreignEvent = SourceSystemEvent.Create(
            foreignTenant.Id,
            foreignWorkspace.Id,
            "capture:foreign",
            "csvUpload",
            "capture.contact.snapshot",
            null,
            null,
            null,
            null,
            "{}",
            "{}",
            "foreign",
            DateTime.UtcNow,
            DateTime.UtcNow);
        fixture.Scout.SourceSystemEvents.Add(foreignEvent);
        await fixture.Scout.SaveChangesAsync();
        await fixture.ExecuteSqlAsync(
            """
            insert into source_capture_generation_members
                ("Id", "TenantId", "ConnectorInstallationId", "Generation", "SourceSystemEventId", "SourceNamespace", "SourceObjectType", "SourceRecordId", "CreatedAtUtc", "UpdatedAtUtc")
            values
                (@id, @tenant, @connector, @generation, @event, @namespace, @objType, @recordId, @now, @now)
            """,
            new NpgsqlParameter("id", Guid.NewGuid()),
            new NpgsqlParameter("tenant", installation.TenantId),
            new NpgsqlParameter("connector", installation.Id),
            new NpgsqlParameter("generation", 1),
            new NpgsqlParameter("event", foreignEvent.Id),
            new NpgsqlParameter("namespace", $"kyntic-connector:{installation.Id:D}"),
            new NpgsqlParameter("objType", "contact"),
            new NpgsqlParameter("recordId", "orphan-x"),
            new NpgsqlParameter("now", DateTime.UtcNow));
        var countMismatch = await RunExporterAsync(fixture.ConnectionString, "proof-tamper", baseOutput, overwrite: true);
        Assert.NotEqual(0, countMismatch.ExitCode);
        Assert.Contains("Selected generation membership contains", countMismatch.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("produced 2", countMismatch.Stderr, StringComparison.OrdinalIgnoreCase);
        await fixture.ExecuteSqlAsync(
            "delete from source_capture_generation_members where \"SourceRecordId\" = @recordId",
            new NpgsqlParameter("recordId", "orphan-x"));

        // 4. Incorrect membership/source identity -> exporter refuses.
        await fixture.ExecuteSqlAsync(
            "update source_capture_generation_members set \"SourceNamespace\" = @namespace where \"Id\" = @id",
            new NpgsqlParameter("namespace", $"kyntic-connector:{Guid.NewGuid():D}"),
            new NpgsqlParameter("id", memberA.Id));
        var wrongIdentity = await RunExporterAsync(fixture.ConnectionString, "proof-tamper", baseOutput, overwrite: true);
        Assert.NotEqual(0, wrongIdentity.ExitCode);
        Assert.Contains("source namespace does not match", wrongIdentity.Stderr, StringComparison.OrdinalIgnoreCase);
        await fixture.ExecuteSqlAsync(
            "update source_capture_generation_members set \"SourceNamespace\" = @namespace where \"Id\" = @id",
            new NpgsqlParameter("namespace", $"kyntic-connector:{installation.Id:D}"),
            new NpgsqlParameter("id", memberA.Id));

        // 5. After all tampering is reverted the export is green again.
        var green = await RunExporterAsync(fixture.ConnectionString, "proof-tamper", baseOutput, overwrite: true);
        Assert.True(green.ExitCode == 0, $"Expected green export after restoring tamper:{Environment.NewLine}{green.Stderr}");
        Assert.Equal(2, (await File.ReadAllLinesAsync(baseOutput)).Count(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static async Task<ExporterResult> RunExporterAsync(
        string connectionString,
        string tenantSlug,
        string outputPath,
        bool overwrite)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(ExporterDllPath());
        startInfo.ArgumentList.Add("--connection-string");
        startInfo.ArgumentList.Add(connectionString);
        startInfo.ArgumentList.Add("--tenant");
        startInfo.ArgumentList.Add(tenantSlug);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        if (overwrite)
        {
            startInfo.ArgumentList.Add("--overwrite");
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the exporter.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ExporterResult(process.ExitCode, stdout, stderr);
    }

    private static string ExporterDllPath()
    {
        var configured = Environment.GetEnvironmentVariable("SCOUT_UPGRADE_EXPORT_DLL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        var dll = Path.Combine(
            root,
            "tools",
            "KynticAI.Scout.UpgradeExport",
            "bin",
            "Debug",
            "net10.0",
            "KynticAI.Scout.UpgradeExport.dll");
        if (!File.Exists(dll))
        {
            throw new InvalidOperationException(
                $"Exporter DLL was not found at '{dll}'. Build the solution before running external proof tests.");
        }
        return dll;
    }

    private static string FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KynticAI.Scout.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }

    private static JsonArray CsvRows(params (string Id, string Name, string ObservedAt)[] rows)
    {
        var array = new JsonArray();
        foreach (var (id, name, observedAt) in rows)
        {
            array.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["observedAtUtc"] = observedAt
            });
        }
        return array;
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = DateTime.SpecifyKind(new DateTime(2026, 8, 15, 9, 0, 0), DateTimeKind.Utc);
    }

    private sealed class PassthroughCredentialStore : IConnectorCredentialStore
    {
        public Task<JsonObject> PersistCredentialsAsync(Guid tenantId, Guid dataSourceId, string connectorType, JsonObject credentials, CancellationToken cancellationToken)
            => Task.FromResult(credentials.DeepClone().AsObject());

        public Task<JsonObject> ResolveConfigurationSecretsAsync(Guid tenantId, JsonObject configuration, CancellationToken cancellationToken)
            => Task.FromResult(configuration.DeepClone().AsObject());
    }

    private sealed class FixtureDb : IAsyncDisposable
    {
        private readonly string _databaseName;

        private FixtureDb(string databaseName, string connectionString, ScoutDbContext scout, TestClock clock, PassthroughCredentialStore credentials)
        {
            _databaseName = databaseName;
            ConnectionString = connectionString;
            Scout = scout;
            Clock = clock;
            Credentials = credentials;
        }

        public string ConnectionString { get; }
        public ScoutDbContext Scout { get; }
        public TestClock Clock { get; }
        public PassthroughCredentialStore Credentials { get; }

        public static async Task<FixtureDb> CreateAsync(string namePrefix)
        {
            var databaseName = $"{namePrefix}_{Guid.NewGuid():N}";
            if (databaseName.Length > 63)
            {
                databaseName = databaseName[..63];
            }

            await using (var admin = new NpgsqlConnection(AdminConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"create database \"{databaseName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var connectionString = DatabaseConnectionString(databaseName);
            var options = new DbContextOptionsBuilder<ScoutDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            var scout = new ScoutDbContext(options);
            await scout.Database.MigrateAsync();
            return new FixtureDb(databaseName, connectionString, scout, new TestClock(), new PassthroughCredentialStore());
        }

        public async Task<(Tenant Tenant, ConnectorInstallation Installation)> AddCsvInstallationAsync(string slug, JsonArray rows)
        {
            var existing = await Scout.Tenants.SingleOrDefaultAsync(t => t.Slug == slug);
            Tenant tenant;
            Workspace workspace;
            if (existing is null)
            {
                tenant = Tenant.Create(slug, slug, Clock.UtcNow);
                workspace = Workspace.Create(tenant.Id, "default", "Default", "Default workspace", true, Clock.UtcNow);
                Scout.Add(tenant);
                Scout.Add(workspace);
                await Scout.SaveChangesAsync();
            }
            else
            {
                tenant = existing;
                workspace = await Scout.Workspaces.SingleAsync(w => w.TenantId == tenant.Id && w.Slug == "default");
            }
            var dataSource = DataSource.Create(
                tenant.Id,
                "Csv Source " + Guid.NewGuid().ToString("N")[..8],
                "CSV fixture source.",
                DataSourceKind.Crm,
                JsonSerializer.Serialize(new JsonObject
                {
                    ["captureFullPermittedPayload"] = true,
                    ["rows"] = rows,
                    ["sourceRecordIdColumn"] = "id",
                    ["sourceRecordIdIsUnique"] = true,
                    ["sourceObjectType"] = "contact",
                    ["observedAtColumn"] = "observedAtUtc",
                    ["redactionPolicyVersion"] = "customer-permitted.v1"
                }),
                Clock.UtcNow);
            var installation = ConnectorInstallation.Create(
                tenant.Id,
                workspace.Id,
                dataSource.Id,
                "csvUpload",
                """["fetchSubject"]""",
                Clock.UtcNow);
            Scout.Add(dataSource);
            Scout.Add(installation);
            await Scout.SaveChangesAsync();
            return (tenant, installation);
        }

        public async Task ChangeCsvRowsAsync(ConnectorInstallation installation, JsonArray rows)
        {
            var dataSource = await Scout.DataSources.SingleAsync(x => x.Id == installation.DataSourceId);
            dataSource.Update(
                dataSource.Name,
                dataSource.Description,
                dataSource.Kind,
                JsonSerializer.Serialize(new JsonObject
                {
                    ["captureFullPermittedPayload"] = true,
                    ["rows"] = rows,
                    ["sourceRecordIdColumn"] = "id",
                    ["sourceRecordIdIsUnique"] = true,
                    ["sourceObjectType"] = "contact",
                    ["observedAtColumn"] = "observedAtUtc",
                    ["redactionPolicyVersion"] = "customer-permitted.v1"
                }),
                Clock.UtcNow);
            await Scout.SaveChangesAsync();
        }

        public FullSourceCaptureCoordinator CreateCoordinator(params IUpgradeSourceCaptureConnector[] connectors)
            => new(Scout, Credentials, connectors, Clock, NullLogger<FullSourceCaptureCoordinator>.Instance);

        public async Task ExecuteSqlAsync(string sql, params NpgsqlParameter[] parameters)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (Scout is not null)
            {
                await Scout.DisposeAsync();
            }

            await using var admin = new NpgsqlConnection(AdminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"drop database if exists \"{_databaseName}\" with (force)",
                admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed record ExporterResult(int ExitCode, string Stdout, string Stderr);
}