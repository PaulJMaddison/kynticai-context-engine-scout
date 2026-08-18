using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Domain.Entities;
using KynticAI.Scout.Domain.Enums;
using KynticAI.Scout.Domain.Saas;
using KynticAI.Scout.Infrastructure.Connectors;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KynticAI.Scout.UnitTests;

public sealed class ContinuityCaptureProofTests
{
    private static readonly DateTime FixedNow =
        new(2026, 08, 16, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SqliteProvider_ConstructsContinuityCaptureSchema()
    {
        using var sqlite = new SqliteConnection("Data Source=:memory:");
        sqlite.Open();
        var options = new DbContextOptionsBuilder<ScoutDbContext>()
            .UseSqlite(sqlite)
            .Options;
        using var db = new ScoutDbContext(options);
        db.Database.EnsureCreated();

        Assert.Equal(0, db.ConnectorCaptureCheckpoints.Count());
        Assert.Equal(0, db.SourceCaptureGenerationMembers.Count());
        Assert.Equal(0, db.SourceCapturePayloadEvidenceRecords.Count());

        var indexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = sqlite.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM pragma_index_list('source_capture_generation_members')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                indexNames.Add(reader.GetString(0));
            }
        }

        Assert.Contains(indexNames, x => x.Contains("TenantId", StringComparison.Ordinal)
            && x.Contains("ConnectorInstallationId", StringComparison.Ordinal)
            && x.Contains("Generation", StringComparison.Ordinal)
            && x.Contains("SourceObjectType", StringComparison.Ordinal)
            && x.Contains("SourceRecordId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Coordinator_FirstGeneration_CompletesMembershipWithProofContracts()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var rows = CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("b", "Bob", "2026-08-15T10:05:00Z"));
        var (installation, dataSource) = fixture.AddCsvInstallation(rows);
        var coordinator = fixture.CreateCoordinator(new CsvFullSourceCaptureConnector());

        var results = await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 10, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.True(result.Executed, result.Reason);
        Assert.Equal(2, result.PersistedRecords);
        Assert.True(result.CompletedGeneration);

        var checkpoint = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == installation.Id);
        Assert.Equal(1, checkpoint.Generation);
        Assert.NotNull(checkpoint.LastFullSourceCompletedAtUtc);
        Assert.Equal(LocalDataPlaneContracts.HistorySnapshotOnly, checkpoint.HistoryCompleteness);
        Assert.Equal(LocalDataPlaneContracts.CurrentStateImmutableSnapshot, checkpoint.CurrentStateConsistency);
        Assert.Equal(LocalDataPlaneContracts.PayloadStorageExactTextV1, checkpoint.PayloadStorageContract);
        Assert.Equal(LocalDataPlaneContracts.GenerationMembershipV1, checkpoint.GenerationMembershipContract);
        Assert.Equal(2, checkpoint.CapturedRecordCount);
        Assert.Null(checkpoint.LeaseOwner);

        Assert.Equal(2, await fixture.Scout.SourceCaptureGenerationMembers.CountAsync(x => x.Generation == 1));
        Assert.Equal(2, await fixture.Scout.SourceCapturePayloadEvidenceRecords.CountAsync());
        Assert.Equal(2, await fixture.Scout.SourceSystemEvents.CountAsync(x => x.TenantId == installation.TenantId));

        var evidence = await fixture.Scout.SourceCapturePayloadEvidenceRecords.FirstAsync(x => x.StorageContract == LocalDataPlaneContracts.PayloadStorageExactTextV1);
        Assert.Equal(LocalDataPlaneContracts.CoverageFullSource, evidence.CoverageScope);
        Assert.False(string.IsNullOrWhiteSpace(evidence.RawPayloadSha256));

        var sourceNamespace = $"kyntic-connector:{installation.Id:D}";
        var members = await fixture.Scout.SourceCaptureGenerationMembers
            .Where(x => x.Generation == 1)
            .OrderBy(x => x.SourceRecordId)
            .ToListAsync();
        Assert.All(members, member => Assert.Equal(sourceNamespace, member.SourceNamespace));
        Assert.Equal(new[] { "a", "b" }, members.Select(x => x.SourceRecordId).ToArray());
        Assert.Equal(dataSource.Id, checkpoint.DataSourceId);
    }

    [Fact]
    public async Task Coordinator_InFlightGenerationIsCheckpointPlusOne_AndDoesNotChangeExportedGeneration()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var rows = CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("b", "Bob", "2026-08-15T10:05:00Z"));
        var (installation, _) = fixture.AddCsvInstallation(rows);
        var coordinator = fixture.CreateCoordinator(new CsvFullSourceCaptureConnector());

        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 1, CancellationToken.None);
        var afterPage1 = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == installation.Id);
        Assert.Equal(0, afterPage1.Generation);
        Assert.False(string.IsNullOrWhiteSpace(afterPage1.ContinuationToken));

        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 1, CancellationToken.None);
        var afterGen1 = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == installation.Id);
        Assert.Equal(1, afterGen1.Generation);
        Assert.Null(afterGen1.ContinuationToken);
        Assert.Equal(2, await fixture.Scout.SourceCaptureGenerationMembers.CountAsync(x => x.Generation == 1));

        fixture.ChangeCsvRows(installation, CsvRows(("a", "Alice", "2026-08-15T10:00:00Z")));
        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 1, CancellationToken.None);
        var afterGen2 = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == installation.Id);
        Assert.Equal(2, afterGen2.Generation);
        Assert.Null(afterGen2.ContinuationToken);
        Assert.Equal(1, await fixture.Scout.SourceCaptureGenerationMembers.CountAsync(x => x.Generation == 2));

        fixture.ChangeCsvRows(installation, CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("c", "Carol", "2026-08-15T10:10:00Z")));
        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 1, CancellationToken.None);
        var afterInFlightGen3 = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == installation.Id);

        Assert.Equal(2, afterInFlightGen3.Generation);
        Assert.False(string.IsNullOrWhiteSpace(afterInFlightGen3.ContinuationToken));
        Assert.Equal(1, await fixture.Scout.SourceCaptureGenerationMembers.CountAsync(x => x.Generation == 3));
        Assert.Equal(1, await fixture.Scout.SourceCaptureGenerationMembers.CountAsync(x => x.Generation == 2));
    }

    [Fact]
    public async Task Coordinator_EmptySource_CompletesProvenEmptyGeneration()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var (installation, _) = fixture.AddCsvInstallation(CsvRows());
        var coordinator = fixture.CreateCoordinator(new CsvFullSourceCaptureConnector());

        var results = await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 10, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.True(result.Executed, result.Reason);
        Assert.Equal(0, result.PersistedRecords);
        Assert.True(result.CompletedGeneration);

        var checkpoint = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == installation.Id);
        Assert.Equal(1, checkpoint.Generation);
        Assert.NotNull(checkpoint.LastFullSourceCompletedAtUtc);
        Assert.Equal(LocalDataPlaneContracts.PayloadStorageExactTextV1, checkpoint.PayloadStorageContract);
        Assert.Equal(LocalDataPlaneContracts.GenerationMembershipV1, checkpoint.GenerationMembershipContract);
        Assert.Equal(0, checkpoint.CapturedRecordCount);
        Assert.Equal(0, await fixture.Scout.SourceCaptureGenerationMembers.CountAsync(x => x.Generation == 1));
        Assert.Equal(0, await fixture.Scout.SourceSystemEvents.CountAsync());
    }

    [Fact]
    public async Task Coordinator_ChangingSourceRowsMidGeneration_FailsClosed()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var rows = CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("b", "Bob", "2026-08-15T10:05:00Z"));
        var (installation, _) = fixture.AddCsvInstallation(rows);
        var coordinator = fixture.CreateCoordinator(new CsvFullSourceCaptureConnector());

        await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 1, CancellationToken.None);
        fixture.ChangeCsvRows(installation, CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("b", "Bob-UPDATED", "2026-08-15T10:06:00Z")));

        var results = await coordinator.RunAllOnceAsync(maxRecordsPerConnector: 1, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.False(result.Executed);
        Assert.Contains("row set changed", result.Reason, StringComparison.OrdinalIgnoreCase);

        var checkpoint = await fixture.Scout.ConnectorCaptureCheckpoints
            .SingleAsync(x => x.ConnectorInstallationId == installation.Id);
        Assert.Equal(0, checkpoint.Generation);
        Assert.Null(checkpoint.LastFullSourceCompletedAtUtc);
        Assert.NotNull(checkpoint.LastError);
        Assert.Null(checkpoint.LeaseOwner);
    }

    [Fact]
    public async Task LegacyCheckpointWithoutMembershipProof_IsNotProvenEmpty()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var (installation, _) = fixture.AddCsvInstallation(CsvRows());
        var legacy = ConnectorCaptureCheckpoint.Create(
            installation.TenantId,
            installation.Id,
            installation.DataSourceId,
            LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
            "1",
            LocalDataPlaneContracts.CoverageFullSource,
            LocalDataPlaneContracts.HistorySnapshotOnly,
            null,
            FixedNow);
        Assert.True(legacy.TryAcquireLease("legacy-writer", TimeSpan.FromMinutes(5), FixedNow));
        legacy.ObserveCaptureSemantics(
            "legacy-writer",
            LocalDataPlaneContracts.HistorySnapshotOnly,
            null,
            FixedNow.AddSeconds(1));
        legacy.Advance(
            "legacy-writer",
            null,
            "{\"rows\":0}",
            0,
            null,
            null,
            FixedNow.AddSeconds(2));
        legacy.CompleteFullSourceGeneration(
            "legacy-writer",
            "{\"rows\":0}",
            LocalDataPlaneContracts.HistorySnapshotOnly,
            FixedNow.AddSeconds(3),
            LocalDataPlaneContracts.PayloadStorageExactTextV1,
            LocalDataPlaneContracts.GenerationMembershipUnknown);
        legacy.ReleaseLease("legacy-writer", FixedNow.AddSeconds(4));
        fixture.Scout.ConnectorCaptureCheckpoints.Add(legacy);
        await fixture.Scout.SaveChangesAsync();

        Assert.Equal(0, await fixture.Scout.SourceCaptureGenerationMembers.CountAsync(x => x.Generation == 1));

        var service = new ScoutUpgradeCompatibilityService(fixture.Scout);
        var manifest = await service.BuildManifestAsync("csv-probe", targetSupportedConnectorTypes: null, CancellationToken.None);

        var descriptor = Assert.Single(manifest.Connectors);
        Assert.Equal(LocalDataPlaneContracts.GenerationMembershipUnknown, descriptor.GenerationMembershipContract);
        Assert.NotEqual(LocalUpgradeReadiness.LosslessDerivedRebuild, manifest.Readiness);
        Assert.Contains(descriptor.Warnings, w => w.Contains("generation membership", StringComparison.OrdinalIgnoreCase));
        Assert.True(manifest.IsSafeForControlPlane);
    }

    [Fact]
    public async Task CsvConnector_PinsImmutableRowSetHashAcrossPages()
    {
        var connector = new CsvFullSourceCaptureConnector();
        var rows = CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("b", "Bob", "2026-08-15T10:05:00Z"),
            ("c", "Carol", "2026-08-15T10:10:00Z"));
        var fixture = ConnectorFixture.ForCsv(rows);

        var page1 = await connector.CaptureBatchAsync(fixture.Request(maxRecords: 1), CancellationToken.None);
        Assert.False(page1.IsComplete);
        Assert.Single(page1.Records);
        Assert.False(string.IsNullOrWhiteSpace(page1.NextContinuationToken));
        Assert.Equal(LocalDataPlaneContracts.CurrentStateImmutableSnapshot, page1.CurrentStateConsistency);
        Assert.Equal(LocalDataPlaneContracts.HistorySnapshotOnly, page1.HistoryCompleteness);

        var page2 = await connector.CaptureBatchAsync(fixture.Request(maxRecords: 1, page1.NextContinuationToken), CancellationToken.None);
        Assert.False(page2.IsComplete);
        Assert.Equal("b", page2.Records[0].SourceRecordId);

        var page3 = await connector.CaptureBatchAsync(fixture.Request(maxRecords: 1, page2.NextContinuationToken), CancellationToken.None);
        Assert.True(page3.IsComplete);
        Assert.Null(page3.NextContinuationToken);
        Assert.Equal("c", page3.Records[0].SourceRecordId);

        using var highWater1 = JsonDocument.Parse(page1.HighWaterMarkJson);
        using var highWater3 = JsonDocument.Parse(page3.HighWaterMarkJson);
        Assert.Equal(
            highWater1.RootElement.GetProperty("snapshotSha256").GetString(),
            highWater3.RootElement.GetProperty("snapshotSha256").GetString());
    }

    [Theory]
    [InlineData(false, true, true, "captureFullPermittedPayload")]
    [InlineData(true, false, true, "sourceRecordIdColumn")]
    [InlineData(true, true, false, "sourceRecordIdIsUnique")]
    public async Task CsvConnector_RejectsMissingContinuityGuards(
        bool captureFullPermittedPayload,
        bool hasRecordIdColumn,
        bool recordIdIsUnique,
        string expectedFragment)
    {
        var connector = new CsvFullSourceCaptureConnector();
        var fixture = ConnectorFixture.ForCsv(
            CsvRows(("a", "Alice", "2026-08-15T10:00:00Z")),
            captureFullPermittedPayload: captureFullPermittedPayload,
            hasRecordIdColumn: hasRecordIdColumn,
            recordIdIsUnique: recordIdIsUnique);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.CaptureBatchAsync(fixture.Request(maxRecords: 10), CancellationToken.None));
        Assert.Contains(expectedFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CsvConnector_RejectsDuplicateSourceRecordIds()
    {
        var connector = new CsvFullSourceCaptureConnector();
        var fixture = ConnectorFixture.ForCsv(CsvRows(
            ("a", "Alice", "2026-08-15T10:00:00Z"),
            ("a", "Alice-Duplicate", "2026-08-15T10:01:00Z")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.CaptureBatchAsync(fixture.Request(maxRecords: 10), CancellationToken.None));
        Assert.Contains("duplicate source record id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqlConnector_RequiresExplicitCaptureColumnsNotSelectorFallback()
    {
        var fixture = ConnectorFixture.ForSql(
            configuration: new JsonObject
            {
                ["mode"] = "currentdatabase",
                ["tableName"] = "orders",
                ["sourceRecordIdColumn"] = "id",
                ["sourceRecordIdIsUnique"] = true,
                ["captureFullPermittedPayload"] = true,
                ["columns"] = new JsonArray("id", "name")
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SqlConnector!.CaptureBatchAsync(fixture.Request(maxRecords: 10), CancellationToken.None));
        Assert.Contains("captureColumns", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqlConnector_RejectsNonUniqueRecordKey()
    {
        var fixture = ConnectorFixture.ForSql(
            configuration: new JsonObject
            {
                ["mode"] = "currentdatabase",
                ["tableName"] = "orders",
                ["sourceRecordIdColumn"] = "id",
                ["sourceRecordIdIsUnique"] = false,
                ["captureFullPermittedPayload"] = true,
                ["captureColumns"] = new JsonArray("id", "name")
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SqlConnector!.CaptureBatchAsync(fixture.Request(maxRecords: 10), CancellationToken.None));
        Assert.Contains("sourceRecordIdIsUnique", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqlConnector_KeysetContinuation_EnumeratesOnlyCaptureColumns()
    {
        var fixture = ConnectorFixture.ForSql(
            configuration: new JsonObject
            {
                ["mode"] = "currentdatabase",
                ["tableName"] = "orders",
                ["sourceRecordIdColumn"] = "id",
                ["sourceRecordIdIsUnique"] = true,
                ["sourceObjectType"] = "order",
                ["observedAtColumn"] = "observed_at",
                ["captureFullPermittedPayload"] = true,
                ["captureColumns"] = new JsonArray("id", "name", "observed_at")
            });

        var page1 = await fixture.SqlConnector!.CaptureBatchAsync(fixture.Request(maxRecords: 2), CancellationToken.None);
        Assert.False(page1.IsComplete);
        Assert.Equal(2, page1.Records.Count);
        Assert.False(string.IsNullOrWhiteSpace(page1.NextContinuationToken));
        Assert.Equal(new[] { "id", "name", "observed_at" }, page1.Records[0].NormalizedPayload.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(LocalDataPlaneContracts.CurrentStateLiveKeyset, page1.CurrentStateConsistency);
        Assert.Equal(LocalDataPlaneContracts.HistorySnapshotOnly, page1.HistoryCompleteness);

        var page2 = await fixture.SqlConnector.CaptureBatchAsync(fixture.Request(maxRecords: 2, page1.NextContinuationToken), CancellationToken.None);
        Assert.True(page2.IsComplete);
        Assert.Single(page2.Records);
        Assert.Equal("o-3", page2.Records[0].SourceRecordId);
    }

    [Fact]
    public async Task RestConnector_RequiresExplicitRetentionDecision()
    {
        var connector = new RestFullSourceCaptureConnector(new FakeHttpClientFactory(
            new StubHttpMessageHandler(_ => JsonResponse("{}"))));
        var baseConfiguration = new JsonObject
        {
            ["baseUrl"] = "https://api.example.com",
            ["capturePathTemplate"] = "/v1/customers",
            ["sourceRecordIdPath"] = "id",
            ["sourceObjectType"] = "customer"
        };

        var missingPermission = baseConfiguration.DeepClone().AsObject();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.CaptureBatchAsync(ConnectorFixture.ForCsv(CsvRows()).RequestWithConfiguration(missingPermission, maxRecords: 10), CancellationToken.None));
        Assert.Contains("captureFullPermittedPayload", exception.Message, StringComparison.OrdinalIgnoreCase);

        var missingRetention = baseConfiguration.DeepClone().AsObject();
        missingRetention["captureFullPermittedPayload"] = true;
        exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.CaptureBatchAsync(ConnectorFixture.ForCsv(CsvRows()).RequestWithConfiguration(missingRetention, maxRecords: 10), CancellationToken.None));
        Assert.Contains("retainEntireResponseObject", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestConnector_CursorAdvances_AndRepeatedCursorFailsClosed()
    {
        var calls = 0;
        var connector = new RestFullSourceCaptureConnector(new FakeHttpClientFactory(
            new StubHttpMessageHandler(request =>
            {
                calls++;
                var cursor = GetQueryParameter(request.RequestUri!.Query, "cursor");
                if (cursor is null)
                {
                    return JsonResponse("""{"items":[{"id":"c-1","updatedAtUtc":"2026-08-15T10:00:00Z"}],"nextCursor":"abc"}""");
                }
                return JsonResponse("""{"items":[{"id":"c-2","updatedAtUtc":"2026-08-15T10:05:00Z"}]}""");
            })));
        var fixture = ConnectorFixture.ForCsv(CsvRows()).WithConfiguration(new JsonObject
        {
            ["baseUrl"] = "https://api.example.com",
            ["capturePathTemplate"] = "/v1/customers",
            ["sourceRecordIdPath"] = "id",
            ["sourceObjectType"] = "customer",
            ["captureObservedAtPath"] = "updatedAtUtc",
            ["captureFullPermittedPayload"] = true,
            ["retainEntireResponseObject"] = true
        });

        var page1 = await connector.CaptureBatchAsync(fixture.Request(maxRecords: 10), CancellationToken.None);
        Assert.False(page1.IsComplete);
        Assert.Equal("abc", page1.NextContinuationToken);
        Assert.Equal(LocalDataPlaneContracts.CurrentStateApiCursor, page1.CurrentStateConsistency);
        Assert.Equal(LocalDataPlaneContracts.HistorySnapshotOnly, page1.HistoryCompleteness);

        var page2 = await connector.CaptureBatchAsync(fixture.Request(maxRecords: 10, "abc"), CancellationToken.None);
        Assert.True(page2.IsComplete);
        Assert.Null(page2.NextContinuationToken);
        Assert.Equal("c-2", page2.Records[0].SourceRecordId);

        Assert.Equal(2, calls);

        var repeating = new RestFullSourceCaptureConnector(new FakeHttpClientFactory(
            new StubHttpMessageHandler(_ => JsonResponse("""{"items":[{"id":"c-1"}],"nextCursor":"same"}"""))));
        var sameException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repeating.CaptureBatchAsync(fixture.Request(maxRecords: 10, "same"), CancellationToken.None));
        Assert.Contains("same continuation cursor", sameException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestConnector_NativePositionIsProvenanceNotUpgradedHistory()
    {
        var connector = new RestFullSourceCaptureConnector(new FakeHttpClientFactory(
            new StubHttpMessageHandler(_ => JsonResponse(
                """{"items":[{"id":"c-1","_syncToken":"token-9","updatedAtUtc":"2026-08-15T10:00:00Z"}]}"""))));
        var fixture = ConnectorFixture.ForCsv(CsvRows()).WithConfiguration(new JsonObject
        {
            ["baseUrl"] = "https://api.example.com",
            ["capturePathTemplate"] = "/v1/customers",
            ["sourceRecordIdPath"] = "id",
            ["sourceObjectType"] = "customer",
            ["captureObservedAtPath"] = "updatedAtUtc",
            ["captureSourcePositionPath"] = "_syncToken",
            ["captureFullPermittedPayload"] = true,
            ["retainEntireResponseObject"] = true,
            ["captureHistoryCompleteness"] = "SNAPSHOT_ONLY"
        });

        var batch = await connector.CaptureBatchAsync(fixture.Request(maxRecords: 10), CancellationToken.None);
        var record = Assert.Single(batch.Records);
        using var position = JsonDocument.Parse(record.SourcePositionJson);
        Assert.Equal("rest-native-position", position.RootElement.GetProperty("kind").GetString());
        Assert.Equal("token-9", position.RootElement.GetProperty("value").GetString());
        Assert.Equal(LocalDataPlaneContracts.HistorySnapshotOnly, record.HistoryCompleteness);
        Assert.Equal(LocalDataPlaneContracts.HistorySnapshotOnly, batch.HistoryCompleteness);
        Assert.Equal(LocalDataPlaneContracts.CurrentStateApiCursor, batch.CurrentStateConsistency);

        var overClaiming = fixture.WithConfiguration(new JsonObject
        {
            ["baseUrl"] = "https://api.example.com",
            ["capturePathTemplate"] = "/v1/customers",
            ["sourceRecordIdPath"] = "id",
            ["sourceObjectType"] = "customer",
            ["captureFullPermittedPayload"] = true,
            ["retainEntireResponseObject"] = true,
            ["captureHistoryCompleteness"] = "COMPLETE"
        });
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.CaptureBatchAsync(overClaiming.Request(maxRecords: 10), CancellationToken.None));
        Assert.Contains("cannot claim exact historical coverage", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestHealthCheck_RetainsHttpStatusCodeDiagnostics()
    {
        var healthyPlugin = new RestApiConnectorPlugin(new FakeHttpClientFactory(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        var degradedPlugin = new RestApiConnectorPlugin(new FakeHttpClientFactory(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var request = new ConnectorHealthCheckRequest(
            "restApi",
            DataSourceKind.Crm,
            new JsonObject { ["baseUrl"] = "https://api.example.com" },
            new JsonObject(),
            null,
            ConnectorRunMode.Preview);

        var healthy = await healthyPlugin.CheckHealthAsync(request, CancellationToken.None);
        var degraded = await degradedPlugin.CheckHealthAsync(request, CancellationToken.None);

        using var healthyJson = JsonDocument.Parse(healthy.DetailsJson);
        using var degradedJson = JsonDocument.Parse(degraded.DetailsJson);
        Assert.Equal(200, healthyJson.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("HEAD", healthyJson.RootElement.GetProperty("method").GetString());
        Assert.Equal(503, degradedJson.RootElement.GetProperty("statusCode").GetInt32());
        Assert.True(healthy.IsHealthy);
        Assert.False(degraded.IsHealthy);
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

    private static string GetQueryParameter(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null!;
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = FixedNow;
    }

    private sealed class PassthroughCredentialStore : IConnectorCredentialStore
    {
        public Task<JsonObject> PersistCredentialsAsync(
            Guid tenantId, Guid dataSourceId, string connectorType, JsonObject credentials, CancellationToken cancellationToken)
            => Task.FromResult(credentials.DeepClone().AsObject());

        public Task<JsonObject> ResolveConfigurationSecretsAsync(
            Guid tenantId, JsonObject configuration, CancellationToken cancellationToken)
            => Task.FromResult(configuration.DeepClone().AsObject());
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _scoutConnection = new("Data Source=:memory:");
        private readonly SqliteConnection _opsConnection = new("Data Source=:memory:");

        public ScoutDbContext Scout { get; private set; } = null!;
        public CustomerOpsDbContext Ops { get; private set; } = null!;
        public TestClock Clock { get; } = new();
        public PassthroughCredentialStore Credentials { get; } = new();

        private SqliteFixture()
        {
        }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var fixture = new SqliteFixture();
            await fixture._scoutConnection.OpenAsync();
            await fixture._opsConnection.OpenAsync();
            fixture.Scout = new ScoutDbContext(new DbContextOptionsBuilder<ScoutDbContext>()
                .UseSqlite(fixture._scoutConnection)
                .Options, fixture.Clock);
            fixture.Ops = new CustomerOpsDbContext(new DbContextOptionsBuilder<CustomerOpsDbContext>()
                .UseSqlite(fixture._opsConnection)
                .Options);
            await fixture.Scout.Database.EnsureCreatedAsync();
            await fixture.Ops.Database.EnsureCreatedAsync();
            return fixture;
        }

        public (ConnectorInstallation Installation, DataSource DataSource) AddCsvInstallation(JsonArray rows)
        {
            var tenant = Tenant.Create("csv-probe", "Csv Probe", Clock.UtcNow);
            var workspace = Workspace.Create(tenant.Id, "default", "Default", "Default workspace", true, Clock.UtcNow);
            var dataSource = DataSource.Create(
                tenant.Id,
                "Csv Source",
                "CSV fixture source.",
                DataSourceKind.Crm,
                JsonSerializer.Serialize(BuildCsvConfiguration(rows)),
                Clock.UtcNow);
            var installation = ConnectorInstallation.Create(
                tenant.Id,
                workspace.Id,
                dataSource.Id,
                "csvUpload",
                """["fetchSubject"]""",
                Clock.UtcNow);
            Scout.AddRange(tenant, workspace, dataSource, installation);
            Scout.SaveChanges();
            return (installation, dataSource);
        }

        public void ChangeCsvRows(ConnectorInstallation installation, JsonArray rows)
        {
            var dataSource = Scout.DataSources.Single(x => x.Id == installation.DataSourceId);
            dataSource.Update(
                dataSource.Name,
                dataSource.Description,
                dataSource.Kind,
                JsonSerializer.Serialize(BuildCsvConfiguration(rows)),
                Clock.UtcNow);
            Scout.SaveChanges();
        }

        public FullSourceCaptureCoordinator CreateCoordinator(params IUpgradeSourceCaptureConnector[] connectors)
            => new(
                Scout,
                Credentials,
                connectors,
                Clock,
                NullLogger<FullSourceCaptureCoordinator>.Instance);

        private static JsonObject BuildCsvConfiguration(JsonArray rows) => new()
        {
            ["captureFullPermittedPayload"] = true,
            ["rows"] = rows,
            ["sourceRecordIdColumn"] = "id",
            ["sourceRecordIdIsUnique"] = true,
            ["sourceObjectType"] = "contact",
            ["observedAtColumn"] = "observedAtUtc",
            ["redactionPolicyVersion"] = "customer-permitted.v1"
        };

        public async ValueTask DisposeAsync()
        {
            if (Scout is not null)
            {
                await Scout.DisposeAsync();
            }
            if (Ops is not null)
            {
                await Ops.DisposeAsync();
            }
            await _scoutConnection.DisposeAsync();
            await _opsConnection.DisposeAsync();
        }
    }

    private sealed class ConnectorFixture
    {
        private ConnectorFixture(
            ConnectorInstallation installation,
            DataSource dataSource,
            JsonObject configuration,
            SqliteConnection? sqliteConnection,
            SqlFullSourceCaptureConnector? sqlConnector)
        {
            Installation = installation;
            DataSource = dataSource;
            Configuration = configuration;
            SqliteConnection = sqliteConnection;
            SqlConnector = sqlConnector;
        }

        public ConnectorInstallation Installation { get; }
        public DataSource DataSource { get; }
        public JsonObject Configuration { get; }
        public SqliteConnection? SqliteConnection { get; }
        public SqlFullSourceCaptureConnector? SqlConnector { get; }

        public static ConnectorFixture ForCsv(
            JsonArray rows,
            bool captureFullPermittedPayload = true,
            bool hasRecordIdColumn = true,
            bool recordIdIsUnique = true)
        {
            var tenant = Tenant.Create("csv-probe", "Csv Probe", FixedNow);
            var dataSource = DataSource.Create(tenant.Id, "Csv Source", "CSV fixture.", DataSourceKind.Crm, "{}", FixedNow);
            var installation = ConnectorInstallation.Create(tenant.Id, Guid.NewGuid(), dataSource.Id, "csvUpload", "[]", FixedNow);
            var configuration = new JsonObject
            {
                ["captureFullPermittedPayload"] = captureFullPermittedPayload,
                ["rows"] = rows,
                ["sourceRecordIdColumn"] = hasRecordIdColumn ? "id" : null,
                ["sourceRecordIdIsUnique"] = recordIdIsUnique,
                ["sourceObjectType"] = "contact",
                ["observedAtColumn"] = "observedAtUtc"
            };
            return new ConnectorFixture(installation, dataSource, configuration, sqliteConnection: null, sqlConnector: null);
        }

        public ConnectorFixture WithConfiguration(JsonObject configuration)
            => new(Installation, DataSource, configuration, SqliteConnection, SqlConnector);

        public ConnectorSourceCaptureRequest Request(int maxRecords, string? continuationToken = null)
            => new(Installation, DataSource, Configuration, new JsonObject(), continuationToken, maxRecords, FixedNow);

        public ConnectorSourceCaptureRequest RequestWithConfiguration(JsonObject configuration, int maxRecords)
            => new(Installation, DataSource, configuration, new JsonObject(), null, maxRecords, FixedNow);

        public static ConnectorFixture ForSql(JsonObject configuration)
        {
            var tenant = Tenant.Create("sql-probe", "Sql Probe", FixedNow);
            var dataSource = DataSource.Create(tenant.Id, "Sql Source", "SQL fixture.", DataSourceKind.SqlMetric, JsonSerializer.Serialize(configuration), FixedNow);
            var installation = ConnectorInstallation.Create(tenant.Id, Guid.NewGuid(), dataSource.Id, "sqlDatabase", "[]", FixedNow);

            var sqlite = new SqliteConnection("Data Source=:memory:");
            sqlite.Open();
            using (var command = sqlite.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE orders (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        observed_at TEXT NOT NULL
                    );
                    INSERT INTO orders (id, name, observed_at) VALUES
                        ('o-1', 'One', '2026-08-15T10:00:00Z'),
                        ('o-2', 'Two', '2026-08-15T10:05:00Z'),
                        ('o-3', 'Three', '2026-08-15T10:10:00Z');
                    """;
                command.ExecuteNonQuery();
            }

            var clock = new TestClock();
            var scout = new ScoutDbContext(new DbContextOptionsBuilder<ScoutDbContext>()
                .UseSqlite(sqlite)
                .Options, clock);
            scout.Database.EnsureCreated();
            scout.Dispose();

            var connector = new SqlFullSourceCaptureConnector(
                new ScoutDbContext(new DbContextOptionsBuilder<ScoutDbContext>().UseSqlite(sqlite).Options, clock),
                new CustomerOpsDbContext(new DbContextOptionsBuilder<CustomerOpsDbContext>().UseSqlite(sqlite).Options));
            return new ConnectorFixture(installation, dataSource, configuration, sqlite, connector);
        }
    }
}