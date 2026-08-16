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
    await using var connection = new NpgsqlConnection(options.ConnectionString);
    await connection.OpenAsync(cancellationToken);

    var tenantId = await ResolveTenantIdAsync(connection, options.TenantSlug, cancellationToken);
    await AssertConnectorBarrierReadyAsync(connection, tenantId, cancellationToken);
    var selections = await LoadSnapshotSelectionsAsync(connection, tenantId, cancellationToken);
    await AssertSelectedGenerationEvidenceAsync(connection, tenantId, cancellationToken);

    var outputPath = Path.GetFullPath(options.OutputPath);
    var outputDirectory = Path.GetDirectoryName(outputPath)
        ?? throw new InvalidOperationException("Output path has no parent directory.");
    Directory.CreateDirectory(outputDirectory);

    if (!options.Overwrite && File.Exists(outputPath))
        throw new InvalidOperationException($"Output file already exists: {outputPath}. Pass --overwrite to replace it.");

    var fileMode = options.Overwrite ? FileMode.Create : FileMode.CreateNew;
    await using var output = new FileStream(
        outputPath,
        fileMode,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 1024 * 1024,
        options: FileOptions.SequentialScan | FileOptions.Asynchronous);
    using var exportHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    // Snapshot-only sources are reconstructed from the latest COMPLETED generation only. Older
    // generations remain in Scout for bounded audit/history but are not replayed as current state.
    // This prevents a record deleted between generations from being resurrected by Fortress.
    const string sql = """
        select
            c."ConnectorInstallationId",
            c."Generation",
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
        from connector_capture_checkpoints c
        inner join source_capture_generation_members gm
            on gm."TenantId" = c."TenantId"
            and gm."ConnectorInstallationId" = c."ConnectorInstallationId"
            and gm."Generation" = c."Generation"
        inner join source_system_events e
            on e."TenantId" = gm."TenantId"
            and e."Id" = gm."SourceSystemEventId"
        inner join source_capture_payload_evidence p
            on p."TenantId" = e."TenantId"
            and p."SourceSystemEventId" = e."Id"
        where c."TenantId" = @tenantId
          and c."LastFullSourceCompletedAtUtc" is not null
          and c."Generation" > 0
          and c."CoverageScope" = 'FULL_SOURCE'
          and c."GenerationMembershipContract" = 'generation-membership.v1'
          and c."HistoryCompleteness" in ('SNAPSHOT_ONLY', 'UNKNOWN')
          and p."CoverageScope" = 'FULL_SOURCE'
          and p."StorageContract" = 'exact-text.v1'
        order by c."ConnectorInstallationId", gm."SourceObjectType", gm."SourceRecordId", e."Id"
        """;

    await using var command = new NpgsqlCommand(sql, connection)
    {
        CommandTimeout = 0
    };
    command.Parameters.AddWithValue("tenantId", tenantId);

    var rowCount = 0L;
    var connectorTypes = new SortedSet<string>(StringComparer.Ordinal);
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
        Guid? workspaceId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8);
        var eventId = reader.GetString(9);
        var sourceSystem = reader.GetString(10);
        var eventType = reader.GetString(11);
        Guid? dataSourceId = reader.IsDBNull(12) ? (Guid?)null : reader.GetGuid(12);
        var exactPayload = reader.GetString(13);
        var headersJson = reader.GetString(14);
        var receivedAtUtc = reader.GetDateTime(15);
        var observedAtUtc = reader.GetDateTime(16);
        var evidenceHash = reader.GetString(17);
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

        connectorTypes.Add(sourceSystem);
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
            $"Selected generation membership contains {expectedRows} row(s) but the exact-evidence export produced {rowCount}. Refusing a partial handoff.");
    }

    var manifestPath = outputPath + ".manifest.json";
    if (!options.Overwrite && File.Exists(manifestPath))
        throw new InvalidOperationException($"Export manifest already exists: {manifestPath}. Pass --overwrite to replace it.");

    var exportManifest = new ScoutJournalExportManifest(
        ExportContract,
        tenantId,
        options.TenantSlug,
        DateTime.UtcNow,
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
        SelectionRule: "SNAPSHOT_ONLY/UNKNOWN connectors export only source_capture_generation_members for the latest completed checkpoint generation. Older snapshot rows are retained locally but are not current-state replay input.",
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

static async Task AssertConnectorBarrierReadyAsync(
    NpgsqlConnection connection,
    Guid tenantId,
    CancellationToken cancellationToken)
{
    const string installationCountSql = "select count(*) from saas_connector_installations where \"TenantId\" = @tenantId";
    await using (var installationCount = new NpgsqlCommand(installationCountSql, connection))
    {
        installationCount.Parameters.AddWithValue("tenantId", tenantId);
        var count = Convert.ToInt64(await installationCount.ExecuteScalarAsync(cancellationToken));
        if (count == 0)
            throw new InvalidOperationException("Scout has no connector installations; an automatic source-continuity export cannot be proven.");
    }

    const string unsafeCheckpointSql = """
        select count(*)
        from saas_connector_installations i
        left join connector_capture_checkpoints c
          on c."TenantId" = i."TenantId"
         and c."ConnectorInstallationId" = i."Id"
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
          )
        """;
    await using var command = new NpgsqlCommand(unsafeCheckpointSql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    var unsafeCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    if (unsafeCount != 0)
    {
        throw new InvalidOperationException(
            $"{unsafeCount} connector installation(s) do not have a completed snapshot-source FULL_SOURCE generation under exact-text.v1 + generation-membership.v1 with declared current-state consistency. Run/repair Scout full-source capture before export. Provider-specific exact-history connectors require their own ordered-history export contract.");
    }
}

static async Task<IReadOnlyList<ScoutSnapshotExportSelection>> LoadSnapshotSelectionsAsync(
    NpgsqlConnection connection,
    Guid tenantId,
    CancellationToken cancellationToken)
{
    const string sql = """
        select
            i."Id",
            i."ConnectorType",
            c."Generation",
            c."LastFullSourceCompletedAtUtc",
            c."HistoryCompleteness",
            c."CurrentStateConsistency",
            c."GenerationMembershipContract",
            count(gm."Id")
        from saas_connector_installations i
        inner join connector_capture_checkpoints c
          on c."TenantId" = i."TenantId"
         and c."ConnectorInstallationId" = i."Id"
        left join source_capture_generation_members gm
          on gm."TenantId" = c."TenantId"
         and gm."ConnectorInstallationId" = c."ConnectorInstallationId"
         and gm."Generation" = c."Generation"
        where i."TenantId" = @tenantId
          and c."LastFullSourceCompletedAtUtc" is not null
        group by
            i."Id",
            i."ConnectorType",
            c."Generation",
            c."LastFullSourceCompletedAtUtc",
            c."HistoryCompleteness",
            c."CurrentStateConsistency",
            c."GenerationMembershipContract"
        order by i."Id"
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);

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
    CancellationToken cancellationToken)
{
    // Check only the generation selected for current-state reconstruction. Old snapshot rows may
    // remain as bounded audit evidence and do not have to be promoted into current replay input.
    const string sql = """
        select count(*)
        from connector_capture_checkpoints c
        inner join source_capture_generation_members gm
          on gm."TenantId" = c."TenantId"
         and gm."ConnectorInstallationId" = c."ConnectorInstallationId"
         and gm."Generation" = c."Generation"
        inner join source_system_events e
          on e."TenantId" = gm."TenantId"
         and e."Id" = gm."SourceSystemEventId"
        left join source_capture_payload_evidence p
          on p."TenantId" = e."TenantId"
         and p."SourceSystemEventId" = e."Id"
        where c."TenantId" = @tenantId
          and (
                p."Id" is null
             or p."StorageContract" <> 'exact-text.v1'
             or p."CoverageScope" <> 'FULL_SOURCE'
             or p."RawPayloadSha256" is null
          )
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    var missing = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    if (missing != 0)
    {
        throw new InvalidOperationException(
            $"{missing} latest-generation member(s) do not have exact customer-local payload evidence. Refusing a partial export.");
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
    bool Overwrite)
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
            flags.Contains("--overwrite"));
    }
}
