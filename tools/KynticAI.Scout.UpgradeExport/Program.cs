using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

const string ExportContract = "kyntic-scout-source-journal-export.v2";
const string CaptureContract = "kyntic-local-source-capture.v1";
const string FullSource = "FULL_SOURCE";
const string ExactTextV1 = "exact-text.v1";
const string GenerationMembershipV1 = "generation-membership.v1";
const string ScoutActive = "ScoutActive";
const string ScoutPausedForCutover = "ScoutPausedForCutover";

try
{
    var options = Options.Parse(args);
    await ExportAsync(options, CancellationToken.None);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Scout upgrade export failed: {exception.Message}");
    return 1;
}

static async Task ExportAsync(Options options, CancellationToken cancellationToken)
{
    var outputPath = Path.GetFullPath(options.OutputPath);
    var outputDirectory = Path.GetDirectoryName(outputPath)
        ?? throw new InvalidOperationException("Output path has no parent directory.");
    var manifestPath = outputPath + ".manifest.json";

    if (!options.Overwrite && File.Exists(outputPath))
        throw new InvalidOperationException($"Output file already exists: {outputPath}. Pass --overwrite to replace it.");
    if (!options.Overwrite && File.Exists(manifestPath))
        throw new InvalidOperationException($"Export manifest already exists: {manifestPath}. Pass --overwrite to replace it.");

    Directory.CreateDirectory(outputDirectory);

    await using var connection = new NpgsqlConnection(options.ConnectionString);
    await connection.OpenAsync(cancellationToken);

    var tenantId = await ResolveTenantIdAsync(connection, options.TenantSlug, cancellationToken);
    var cutoverTokenSha256 = Sha256(options.CutoverToken);

    // Establish the durable ownership barrier before reading any export selection. This transaction
    // locks every connector checkpoint, refuses a worker-owned lease, and persists the exact
    // generation/high-water binding for the supplied epoch/token. Once committed, normal Scout
    // capture fails closed on the ownership row and the export cannot drift to a newer generation.
    await PauseScoutForCutoverAsync(
        connection,
        tenantId,
        options.CutoverEpoch,
        cutoverTokenSha256,
        cancellationToken);

    await AssertConnectorBarrierReadyAsync(
        connection,
        tenantId,
        options.CutoverEpoch,
        cutoverTokenSha256,
        cancellationToken);
    await AssertHighWaterBindingsAsync(
        connection,
        tenantId,
        options.CutoverEpoch,
        cutoverTokenSha256,
        cancellationToken);
    var selections = await LoadSnapshotSelectionsAsync(
        connection,
        tenantId,
        options.CutoverEpoch,
        cutoverTokenSha256,
        cancellationToken);
    await AssertSelectedGenerationEvidenceAsync(
        connection,
        tenantId,
        options.CutoverEpoch,
        cutoverTokenSha256,
        cancellationToken);

    var selectionsByConnector = selections.ToDictionary(x => x.ConnectorInstallationId);
    var connectorTypes = new SortedSet<string>(
        selections.Select(x => x.ConnectorType),
        StringComparer.Ordinal);

    var fileMode = options.Overwrite ? FileMode.Create : FileMode.CreateNew;
    await using var output = new FileStream(
        outputPath,
        fileMode,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 1024 * 1024,
        options: FileOptions.SequentialScan | FileOptions.Asynchronous);
    using var exportHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    const string sql = """
        select
            o."ConnectorInstallationId",
            o."SelectedGeneration",
            c."HistoryCompleteness",
            c."CurrentStateConsistency",
            gm."SourceNamespace",
            gm."SourceObjectType",
            gm."SourceRecordId",
            e."TenantId",
            e."WorkspaceId",
            e."EventId",
            e."SourceSystem",
            e."EventType",
            e."DataSourceId",
            p."ExactPayloadText",
            e."HeadersJson"::text,
            e."ReceivedAtUtc",
            e."ObservedAtUtc",
            p."RawPayloadSha256"
        from connector_capture_ownership o
        inner join connector_capture_checkpoints c
            on c."TenantId" = o."TenantId"
            and c."ConnectorInstallationId" = o."ConnectorInstallationId"
            and c."Generation" = o."SelectedGeneration"
            and c."LastFullSourceCompletedAtUtc" = o."SnapshotCompletedAtUtc"
        inner join source_capture_generation_members gm
            on gm."TenantId" = o."TenantId"
            and gm."ConnectorInstallationId" = o."ConnectorInstallationId"
            and gm."Generation" = o."SelectedGeneration"
        inner join source_system_events e
            on e."TenantId" = gm."TenantId"
            and e."Id" = gm."SourceSystemEventId"
        inner join source_capture_payload_evidence p
            on p."TenantId" = e."TenantId"
            and p."SourceSystemEventId" = e."Id"
        where o."TenantId" = @tenantId
          and o."State" = @pausedState
          and o."CutoverEpoch" = @cutoverEpoch
          and o."CutoverTokenSha256" = @cutoverTokenSha256
          and c."CoverageScope" = 'FULL_SOURCE'
          and c."GenerationMembershipContract" = 'generation-membership.v1'
          and c."HistoryCompleteness" in ('SNAPSHOT_ONLY', 'UNKNOWN')
          and p."CoverageScope" = 'FULL_SOURCE'
          and p."StorageContract" = 'exact-text.v1'
        order by o."ConnectorInstallationId", gm."SourceObjectType", gm."SourceRecordId", e."Id"
        """;

    await using var command = new NpgsqlCommand(sql, connection)
    {
        CommandTimeout = 0
    };
    command.Parameters.AddWithValue("tenantId", tenantId);
    command.Parameters.AddWithValue("pausedState", ScoutPausedForCutover);
    command.Parameters.AddWithValue("cutoverEpoch", options.CutoverEpoch);
    command.Parameters.AddWithValue("cutoverTokenSha256", cutoverTokenSha256);

    var rowCount = 0L;
    await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        var connectorInstallationId = reader.GetGuid(0);
        var generation = reader.GetInt64(1);
        var historyCompleteness = reader.GetString(2);
        var currentStateConsistency = reader.GetString(3);
        var sourceNamespace = reader.GetString(4);
        var sourceObjectType = reader.GetString(5);
        var sourceRecordId = reader.GetString(6);
        var rowTenantId = reader.GetGuid(7);
        if (rowTenantId != tenantId)
            throw new InvalidOperationException($"Export row {rowCount + 1} tenant does not match the selected Scout tenant.");
        Guid? workspaceId = reader.IsDBNull(8) ? null : reader.GetGuid(8);
        var eventId = reader.GetString(9);
        var sourceSystem = reader.GetString(10);
        var eventType = reader.GetString(11);
        Guid? dataSourceId = reader.IsDBNull(12) ? null : reader.GetGuid(12);
        var exactPayload = reader.GetString(13);
        var headersJson = reader.GetString(14);
        var receivedAtUtc = reader.GetDateTime(15);
        var observedAtUtc = reader.GetDateTime(16);
        var evidenceHash = reader.GetString(17);

        if (!selectionsByConnector.TryGetValue(connectorInstallationId, out var selection))
            throw new InvalidOperationException($"Export row {rowCount + 1} has no paused connector selection.");
        if (selection.Generation != generation)
            throw new InvalidOperationException($"Export row {rowCount + 1} generation does not match the paused connector selection.");
        if (!string.Equals(selection.ConnectorType, sourceSystem, StringComparison.Ordinal))
            throw new InvalidOperationException($"Export row {rowCount + 1} source system does not match its connector installation.");

        var actualHash = Sha256(exactPayload);
        if (!string.Equals(actualHash, evidenceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Exact payload evidence hash mismatch at exported row {rowCount + 1}.");

        ValidateCaptureEnvelope(
            headersJson,
            actualHash,
            rowCount + 1,
            connectorInstallationId,
            sourceNamespace,
            sourceObjectType,
            sourceRecordId);

        var row = new ScoutJournalExportRow(
            connectorInstallationId,
            generation,
            historyCompleteness,
            currentStateConsistency,
            sourceNamespace,
            sourceObjectType,
            sourceRecordId,
            rowTenantId,
            workspaceId,
            eventId,
            sourceSystem,
            eventType,
            dataSourceId,
            exactPayload,
            headersJson,
            receivedAtUtc,
            observedAtUtc);

        var json = JsonSerializer.Serialize(row);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await output.WriteAsync(bytes, cancellationToken);
        exportHash.AppendData(bytes);
        rowCount++;
    }

    await output.FlushAsync(cancellationToken);
    output.Flush(flushToDisk: true);
    var fileSha256 = Convert.ToHexString(exportHash.GetHashAndReset()).ToLowerInvariant();

    var expectedRows = selections.Sum(x => x.MemberCount);
    if (rowCount != expectedRows)
    {
        throw new InvalidOperationException(
            $"Paused generation membership contains {expectedRows} row(s) but the exact-evidence export produced {rowCount}. Refusing a partial handoff.");
    }

    var exportManifest = new ScoutJournalExportManifest(
        ExportContract,
        tenantId,
        options.TenantSlug,
        DateTime.UtcNow,
        options.CutoverEpoch,
        cutoverTokenSha256,
        rowCount,
        fileSha256,
        ExactTextV1,
        GenerationMembershipV1,
        connectorTypes.ToArray(),
        selections,
        CustomerDataRemainsLocal: true,
        ContainsCredentialValues: false,
        ContainsProtectedCredentialReferences: false,
        ContainsExactCustomerPayloads: true,
        SelectionRule: "Export is bound to connector_capture_ownership rows in ScoutPausedForCutover for the supplied cutover epoch/token hash; only the persisted selected generation is replay input.",
        Purpose: "Customer-local Scout -> Fortress governed-state rebuild only. Do not upload this file to KynticAI Cloud.");

    await File.WriteAllTextAsync(
        manifestPath,
        JsonSerializer.Serialize(exportManifest, new JsonSerializerOptions { WriteIndented = true }),
        Encoding.UTF8,
        cancellationToken);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        contract = ExportContract,
        tenantId,
        tenant = options.TenantSlug,
        cutoverEpoch = options.CutoverEpoch,
        cutoverTokenSha256,
        rows = rowCount,
        sha256 = fileSha256,
        payloadStorageContract = ExactTextV1,
        generationMembershipContract = GenerationMembershipV1,
        connectorTypes,
        connectorSelections = selections.Select(x => new
        {
            x.ConnectorInstallationId,
            x.ConnectorType,
            x.Generation,
            x.LastFullSourceCompletedAtUtc,
            x.HistoryCompleteness,
            x.CurrentStateConsistency,
            x.MemberCount
        }),
        scoutPausedForCutover = true,
        customerPayloadsPrinted = false,
        customerDataRemainsLocal = true,
        output = outputPath,
        manifest = manifestPath
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task<Guid> ResolveTenantIdAsync(
    NpgsqlConnection connection,
    string tenantSlug,
    CancellationToken cancellationToken)
{
    const string sql = "select \"Id\" from tenants where \"Slug\" = @slug";
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("slug", tenantSlug.Trim().ToLowerInvariant());
    var value = await command.ExecuteScalarAsync(cancellationToken)
        ?? throw new InvalidOperationException($"Scout tenant '{tenantSlug}' was not found.");
    return (Guid)value;
}

static async Task PauseScoutForCutoverAsync(
    NpgsqlConnection connection,
    Guid tenantId,
    Guid cutoverEpoch,
    string cutoverTokenSha256,
    CancellationToken cancellationToken)
{
    var utcNow = DateTime.UtcNow;
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

    const string installationCountSql = "select count(*) from saas_connector_installations where \"TenantId\" = @tenantId";
    await using var installationCountCommand = new NpgsqlCommand(installationCountSql, connection, transaction);
    installationCountCommand.Parameters.AddWithValue("tenantId", tenantId);
    var installationCount = Convert.ToInt64(await installationCountCommand.ExecuteScalarAsync(cancellationToken));
    if (installationCount == 0)
        throw new InvalidOperationException("Scout has no connector installations; an automatic source-continuity export cannot be proven.");

    const string checkpointSql = """
        select
            c."ConnectorInstallationId",
            c."Generation",
            c."LastFullSourceCompletedAtUtc",
            c."HighWaterMarkJson"::text,
            c."ContinuationToken",
            c."LastError",
            c."LeaseOwner",
            c."LeaseExpiresAtUtc"
        from connector_capture_checkpoints c
        inner join saas_connector_installations i
          on i."TenantId" = c."TenantId"
         and i."Id" = c."ConnectorInstallationId"
        where c."TenantId" = @tenantId
        order by c."ConnectorInstallationId"
        for update of c
        """;

    var checkpoints = new List<CutoverCheckpoint>();
    await using (var checkpointCommand = new NpgsqlCommand(checkpointSql, connection, transaction))
    {
        checkpointCommand.Parameters.AddWithValue("tenantId", tenantId);
        await using var checkpointReader = await checkpointCommand.ExecuteReaderAsync(cancellationToken);
        while (await checkpointReader.ReadAsync(cancellationToken))
        {
            checkpoints.Add(new CutoverCheckpoint(
                checkpointReader.GetGuid(0),
                checkpointReader.GetInt64(1),
                checkpointReader.IsDBNull(2) ? null : checkpointReader.GetDateTime(2),
                checkpointReader.GetString(3),
                checkpointReader.IsDBNull(4) ? null : checkpointReader.GetString(4),
                checkpointReader.IsDBNull(5) ? null : checkpointReader.GetString(5),
                checkpointReader.IsDBNull(6) ? null : checkpointReader.GetString(6),
                checkpointReader.IsDBNull(7) ? null : checkpointReader.GetDateTime(7)));
        }
    }

    if (checkpoints.Count != installationCount)
    {
        throw new InvalidOperationException(
            $"Scout has {installationCount} connector installation(s) but only {checkpoints.Count} capture checkpoint(s). Complete FULL_SOURCE capture for every connector before cutover.");
    }

    foreach (var checkpoint in checkpoints)
    {
        if (checkpoint.Generation <= 0 || checkpoint.LastFullSourceCompletedAtUtc is null)
            throw new InvalidOperationException($"Connector {checkpoint.ConnectorInstallationId} has no completed generation to bind to cutover.");
        if (!string.IsNullOrWhiteSpace(checkpoint.ContinuationToken))
            throw new InvalidOperationException($"Connector {checkpoint.ConnectorInstallationId} has an in-flight paged generation.");
        if (!string.IsNullOrWhiteSpace(checkpoint.LastError))
            throw new InvalidOperationException($"Connector {checkpoint.ConnectorInstallationId} records a capture error.");

        // Cutover is stricter than normal worker lease recovery. A non-empty owner may represent a
        // still-running worker whose timestamp merely expired during a slow operation; automatic
        // cutover must never overlap that worker. A genuinely abandoned lease needs explicit local
        // operator recovery before export.
        if (!string.IsNullOrWhiteSpace(checkpoint.LeaseOwner))
        {
            throw new InvalidOperationException(
                $"Connector {checkpoint.ConnectorInstallationId} still has a Scout capture lease owner '{checkpoint.LeaseOwner}'. Clear/recover the local worker before cutover.");
        }

        var highWaterMarkSha256 = Sha256(checkpoint.HighWaterMarkJson);
        const string ownershipSql = """
            insert into connector_capture_ownership
            (
                "Id", "ConnectorInstallationId", "State", "SelectedGeneration",
                "SnapshotCompletedAtUtc", "HighWaterMarkSha256", "CutoverEpoch",
                "CutoverTokenSha256", "ScoutPausedAtUtc", "FortressOwnedAtUtc",
                "CreatedAtUtc", "UpdatedAtUtc", "TenantId"
            )
            values
            (
                @id, @connectorInstallationId, @pausedState, @selectedGeneration,
                @snapshotCompletedAtUtc, @highWaterMarkSha256, @cutoverEpoch,
                @cutoverTokenSha256, @utcNow, null, @utcNow, @utcNow, @tenantId
            )
            on conflict ("TenantId", "ConnectorInstallationId") do update set
                "State" = @pausedState,
                "SelectedGeneration" = excluded."SelectedGeneration",
                "SnapshotCompletedAtUtc" = excluded."SnapshotCompletedAtUtc",
                "HighWaterMarkSha256" = excluded."HighWaterMarkSha256",
                "CutoverEpoch" = excluded."CutoverEpoch",
                "CutoverTokenSha256" = excluded."CutoverTokenSha256",
                "ScoutPausedAtUtc" = case
                    when connector_capture_ownership."State" = @pausedState
                        then connector_capture_ownership."ScoutPausedAtUtc"
                    else excluded."ScoutPausedAtUtc"
                end,
                "FortressOwnedAtUtc" = null,
                "UpdatedAtUtc" = @utcNow
            where connector_capture_ownership."State" = @activeState
               or (
                    connector_capture_ownership."State" = @pausedState
                    and connector_capture_ownership."CutoverEpoch" = @cutoverEpoch
                    and connector_capture_ownership."CutoverTokenSha256" = @cutoverTokenSha256
                    and connector_capture_ownership."SelectedGeneration" = excluded."SelectedGeneration"
                    and connector_capture_ownership."SnapshotCompletedAtUtc" = excluded."SnapshotCompletedAtUtc"
                    and connector_capture_ownership."HighWaterMarkSha256" = excluded."HighWaterMarkSha256"
               )
            returning "State"
            """;

        await using var ownershipCommand = new NpgsqlCommand(ownershipSql, connection, transaction);
        ownershipCommand.Parameters.AddWithValue("id", Guid.NewGuid());
        ownershipCommand.Parameters.AddWithValue("connectorInstallationId", checkpoint.ConnectorInstallationId);
        ownershipCommand.Parameters.AddWithValue("activeState", ScoutActive);
        ownershipCommand.Parameters.AddWithValue("pausedState", ScoutPausedForCutover);
        ownershipCommand.Parameters.AddWithValue("selectedGeneration", checkpoint.Generation);
        ownershipCommand.Parameters.AddWithValue("snapshotCompletedAtUtc", checkpoint.LastFullSourceCompletedAtUtc.Value);
        ownershipCommand.Parameters.AddWithValue("highWaterMarkSha256", highWaterMarkSha256);
        ownershipCommand.Parameters.AddWithValue("cutoverEpoch", cutoverEpoch);
        ownershipCommand.Parameters.AddWithValue("cutoverTokenSha256", cutoverTokenSha256);
        ownershipCommand.Parameters.AddWithValue("utcNow", utcNow);
        ownershipCommand.Parameters.AddWithValue("tenantId", tenantId);

        var state = await ownershipCommand.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.Equals(state, ScoutPausedForCutover, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Connector {checkpoint.ConnectorInstallationId} is already owned by another or mismatched cutover/Fortress binding. Refusing to overwrite source ownership.");
        }
    }

    await transaction.CommitAsync(cancellationToken);
}

static async Task AssertConnectorBarrierReadyAsync(
    NpgsqlConnection connection,
    Guid tenantId,
    Guid cutoverEpoch,
    string cutoverTokenSha256,
    CancellationToken cancellationToken)
{
    const string sql = """
        select count(*)
        from saas_connector_installations i
        left join connector_capture_checkpoints c
          on c."TenantId" = i."TenantId"
         and c."ConnectorInstallationId" = i."Id"
        left join connector_capture_ownership o
          on o."TenantId" = i."TenantId"
         and o."ConnectorInstallationId" = i."Id"
        where i."TenantId" = @tenantId
          and (
                c."Id" is null
             or c."LastFullSourceCompletedAtUtc" is null
             or c."Generation" <= 0
             or c."CoverageScope" <> 'FULL_SOURCE'
             or c."PayloadStorageContract" <> 'exact-text.v1'
             or c."GenerationMembershipContract" <> 'generation-membership.v1'
             or c."CurrentStateConsistency" = 'UNKNOWN'
             or c."HistoryCompleteness" not in ('SNAPSHOT_ONLY', 'UNKNOWN')
             or o."Id" is null
             or o."State" <> @pausedState
             or o."CutoverEpoch" <> @cutoverEpoch
             or o."CutoverTokenSha256" <> @cutoverTokenSha256
             or o."SelectedGeneration" <> c."Generation"
             or o."SnapshotCompletedAtUtc" <> c."LastFullSourceCompletedAtUtc"
          )
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    command.Parameters.AddWithValue("pausedState", ScoutPausedForCutover);
    command.Parameters.AddWithValue("cutoverEpoch", cutoverEpoch);
    command.Parameters.AddWithValue("cutoverTokenSha256", cutoverTokenSha256);
    var unsafeCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    if (unsafeCount != 0)
    {
        throw new InvalidOperationException(
            $"{unsafeCount} connector installation(s) do not have an exact persisted Scout-paused cutover binding. Refusing export.");
    }
}

static async Task AssertHighWaterBindingsAsync(
    NpgsqlConnection connection,
    Guid tenantId,
    Guid cutoverEpoch,
    string cutoverTokenSha256,
    CancellationToken cancellationToken)
{
    const string sql = """
        select
            o."ConnectorInstallationId",
            o."HighWaterMarkSha256",
            c."HighWaterMarkJson"::text
        from connector_capture_ownership o
        inner join connector_capture_checkpoints c
          on c."TenantId" = o."TenantId"
         and c."ConnectorInstallationId" = o."ConnectorInstallationId"
        where o."TenantId" = @tenantId
          and o."State" = @pausedState
          and o."CutoverEpoch" = @cutoverEpoch
          and o."CutoverTokenSha256" = @cutoverTokenSha256
        order by o."ConnectorInstallationId"
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    command.Parameters.AddWithValue("pausedState", ScoutPausedForCutover);
    command.Parameters.AddWithValue("cutoverEpoch", cutoverEpoch);
    command.Parameters.AddWithValue("cutoverTokenSha256", cutoverTokenSha256);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        var connectorInstallationId = reader.GetGuid(0);
        var persistedHash = reader.GetString(1);
        var checkpointJson = reader.GetString(2);
        var actualHash = Sha256(checkpointJson);
        if (!string.Equals(persistedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Connector {connectorInstallationId} high-water mark no longer matches its paused cutover binding.");
        }
    }
}

static async Task<IReadOnlyList<ScoutSnapshotExportSelection>> LoadSnapshotSelectionsAsync(
    NpgsqlConnection connection,
    Guid tenantId,
    Guid cutoverEpoch,
    string cutoverTokenSha256,
    CancellationToken cancellationToken)
{
    const string sql = """
        select
            i."Id",
            i."ConnectorType",
            o."SelectedGeneration",
            o."SnapshotCompletedAtUtc",
            c."HistoryCompleteness",
            c."CurrentStateConsistency",
            c."GenerationMembershipContract",
            count(gm."Id")
        from saas_connector_installations i
        inner join connector_capture_ownership o
          on o."TenantId" = i."TenantId"
         and o."ConnectorInstallationId" = i."Id"
        inner join connector_capture_checkpoints c
          on c."TenantId" = o."TenantId"
         and c."ConnectorInstallationId" = o."ConnectorInstallationId"
         and c."Generation" = o."SelectedGeneration"
         and c."LastFullSourceCompletedAtUtc" = o."SnapshotCompletedAtUtc"
        left join source_capture_generation_members gm
          on gm."TenantId" = o."TenantId"
         and gm."ConnectorInstallationId" = o."ConnectorInstallationId"
         and gm."Generation" = o."SelectedGeneration"
        where i."TenantId" = @tenantId
          and o."State" = @pausedState
          and o."CutoverEpoch" = @cutoverEpoch
          and o."CutoverTokenSha256" = @cutoverTokenSha256
        group by
            i."Id", i."ConnectorType", o."SelectedGeneration", o."SnapshotCompletedAtUtc",
            c."HistoryCompleteness", c."CurrentStateConsistency", c."GenerationMembershipContract"
        order by i."Id"
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    command.Parameters.AddWithValue("pausedState", ScoutPausedForCutover);
    command.Parameters.AddWithValue("cutoverEpoch", cutoverEpoch);
    command.Parameters.AddWithValue("cutoverTokenSha256", cutoverTokenSha256);

    var selections = new List<ScoutSnapshotExportSelection>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        selections.Add(new ScoutSnapshotExportSelection(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetDateTime(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7)));
    }
    return selections;
}

static async Task AssertSelectedGenerationEvidenceAsync(
    NpgsqlConnection connection,
    Guid tenantId,
    Guid cutoverEpoch,
    string cutoverTokenSha256,
    CancellationToken cancellationToken)
{
    const string sql = """
        select count(*)
        from connector_capture_ownership o
        inner join source_capture_generation_members gm
          on gm."TenantId" = o."TenantId"
         and gm."ConnectorInstallationId" = o."ConnectorInstallationId"
         and gm."Generation" = o."SelectedGeneration"
        inner join source_system_events e
          on e."TenantId" = gm."TenantId"
         and e."Id" = gm."SourceSystemEventId"
        left join source_capture_payload_evidence p
          on p."TenantId" = e."TenantId"
         and p."SourceSystemEventId" = e."Id"
        where o."TenantId" = @tenantId
          and o."State" = @pausedState
          and o."CutoverEpoch" = @cutoverEpoch
          and o."CutoverTokenSha256" = @cutoverTokenSha256
          and (
                p."Id" is null
             or p."StorageContract" <> 'exact-text.v1'
             or p."CoverageScope" <> 'FULL_SOURCE'
             or p."RawPayloadSha256" is null
          )
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    command.Parameters.AddWithValue("pausedState", ScoutPausedForCutover);
    command.Parameters.AddWithValue("cutoverEpoch", cutoverEpoch);
    command.Parameters.AddWithValue("cutoverTokenSha256", cutoverTokenSha256);
    var missing = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    if (missing != 0)
    {
        throw new InvalidOperationException(
            $"{missing} paused-generation member(s) do not have exact customer-local payload evidence. Refusing a partial export.");
    }
}

static void ValidateCaptureEnvelope(
    string headersJson,
    string exactPayloadHash,
    long rowNumber,
    Guid connectorInstallationId,
    string sourceNamespace,
    string sourceObjectType,
    string sourceRecordId)
{
    using var document = JsonDocument.Parse(headersJson);
    var root = document.RootElement;
    if (!root.TryGetProperty("kynticCapture", out var capture))
        throw new InvalidOperationException($"Export row {rowNumber} has no kynticCapture envelope.");

    var contract = RequiredString(capture, "Contract", rowNumber);
    if (!string.Equals(contract, CaptureContract, StringComparison.Ordinal))
        throw new InvalidOperationException($"Export row {rowNumber} uses unsupported capture contract '{contract}'.");

    var coverage = RequiredString(capture, "CoverageScope", rowNumber);
    if (!string.Equals(coverage, FullSource, StringComparison.Ordinal))
        throw new InvalidOperationException($"Export row {rowNumber} is not FULL_SOURCE capture material.");

    var storage = RequiredString(capture, "PayloadStorageContract", rowNumber);
    if (!string.Equals(storage, ExactTextV1, StringComparison.Ordinal))
        throw new InvalidOperationException($"Export row {rowNumber} does not declare exact-text.v1 payload storage.");

    var declaredHash = RequiredString(capture, "RawPayloadSha256", rowNumber);
    if (!string.Equals(declaredHash, exactPayloadHash, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Export row {rowNumber} capture metadata hash does not match exact payload evidence.");

    var declaredConnector = RequiredString(capture, "ConnectorInstanceId", rowNumber);
    if (!Guid.TryParse(declaredConnector, out var parsedConnector) || parsedConnector != connectorInstallationId)
        throw new InvalidOperationException($"Export row {rowNumber} connector installation does not match generation membership.");

    var declaredNamespace = RequiredString(capture, "SourceNamespace", rowNumber);
    if (!string.Equals(declaredNamespace, sourceNamespace, StringComparison.Ordinal))
        throw new InvalidOperationException($"Export row {rowNumber} source namespace does not match generation membership.");

    var declaredObjectType = RequiredString(capture, "SourceObjectType", rowNumber);
    if (!string.Equals(declaredObjectType, sourceObjectType, StringComparison.Ordinal))
        throw new InvalidOperationException($"Export row {rowNumber} source object type does not match generation membership.");

    var declaredRecordId = RequiredString(capture, "SourceRecordId", rowNumber);
    if (!string.Equals(declaredRecordId, sourceRecordId, StringComparison.Ordinal))
        throw new InvalidOperationException($"Export row {rowNumber} source record id does not match generation membership.");
}

static string RequiredString(JsonElement parent, string propertyName, long rowNumber)
{
    if (!parent.TryGetProperty(propertyName, out var value)
        || value.ValueKind != JsonValueKind.String
        || string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw new InvalidOperationException($"Export row {rowNumber} capture metadata is missing '{propertyName}'.");
    }
    return value.GetString()!;
}

static string Sha256(string value)
    => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

sealed record CutoverCheckpoint(
    Guid ConnectorInstallationId,
    long Generation,
    DateTime? LastFullSourceCompletedAtUtc,
    string HighWaterMarkJson,
    string? ContinuationToken,
    string? LastError,
    string? LeaseOwner,
    DateTime? LeaseExpiresAtUtc);

sealed record ScoutJournalExportRow(
    Guid ConnectorInstallationId,
    long CaptureGeneration,
    string HistoryCompleteness,
    string CurrentStateConsistency,
    string SourceNamespace,
    string SourceObjectType,
    string SourceRecordId,
    Guid TenantId,
    Guid? WorkspaceId,
    string EventId,
    string SourceSystem,
    string EventType,
    Guid? DataSourceId,
    string PayloadJson,
    string HeadersJson,
    DateTime ReceivedAtUtc,
    DateTime ObservedAtUtc);

sealed record ScoutSnapshotExportSelection(
    Guid ConnectorInstallationId,
    string ConnectorType,
    long Generation,
    DateTime LastFullSourceCompletedAtUtc,
    string HistoryCompleteness,
    string CurrentStateConsistency,
    string GenerationMembershipContract,
    long MemberCount);

sealed record ScoutJournalExportManifest(
    string Contract,
    Guid TenantId,
    string TenantSlug,
    DateTime GeneratedAtUtc,
    Guid CutoverEpoch,
    string CutoverTokenSha256,
    long Rows,
    string JournalSha256,
    string PayloadStorageContract,
    string GenerationMembershipContract,
    IReadOnlyList<string> ConnectorTypes,
    IReadOnlyList<ScoutSnapshotExportSelection> ConnectorSelections,
    bool CustomerDataRemainsLocal,
    bool ContainsCredentialValues,
    bool ContainsProtectedCredentialReferences,
    bool ContainsExactCustomerPayloads,
    string SelectionRule,
    string Purpose);

sealed record Options(
    string ConnectionString,
    string TenantSlug,
    string OutputPath,
    bool Overwrite,
    Guid CutoverEpoch,
    string CutoverToken)
{
    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{token}'.");
            if (string.Equals(token, "--overwrite", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add(token);
                continue;
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Argument '{token}' requires a value.");
            values[token] = args[++index];
        }

        var connectionString = values.GetValueOrDefault("--connection-string")
            ?? Environment.GetEnvironmentVariable("SCOUT_UPGRADE_CONNECTION_STRING")
            ?? throw new ArgumentException("Provide --connection-string or SCOUT_UPGRADE_CONNECTION_STRING.");
        var tenantSlug = values.GetValueOrDefault("--tenant")
            ?? throw new ArgumentException("Provide --tenant <tenant-slug>.");
        var output = values.GetValueOrDefault("--output")
            ?? throw new ArgumentException("Provide --output <customer-local-jsonl-path>.");
        var cutoverEpochText = values.GetValueOrDefault("--cutover-epoch")
            ?? throw new ArgumentException("Provide --cutover-epoch <guid>.");
        var cutoverToken = values.GetValueOrDefault("--cutover-token")
            ?? Environment.GetEnvironmentVariable("SCOUT_CUTOVER_TOKEN")
            ?? throw new ArgumentException("Provide --cutover-token or SCOUT_CUTOVER_TOKEN.");

        if (!Guid.TryParse(cutoverEpochText, out var cutoverEpoch) || cutoverEpoch == Guid.Empty)
            throw new ArgumentException("--cutover-epoch must be a non-empty GUID.");
        if (string.IsNullOrWhiteSpace(cutoverToken) || cutoverToken.Length < 32)
            throw new ArgumentException("Cutover token must contain at least 32 characters of local entropy.");
        if (string.IsNullOrWhiteSpace(connectionString)
            || string.IsNullOrWhiteSpace(tenantSlug)
            || string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("Connection string, tenant and output path must be non-empty.");
        }

        return new Options(
            connectionString,
            tenantSlug.Trim().ToLowerInvariant(),
            output,
            flags.Contains("--overwrite"),
            cutoverEpoch,
            cutoverToken);
    }
}
