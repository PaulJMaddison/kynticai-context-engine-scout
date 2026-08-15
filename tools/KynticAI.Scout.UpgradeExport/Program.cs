using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

const string ExportContract = "kyntic-scout-source-journal-export.v1";
const string CaptureContract = "kyntic-local-source-capture.v1";
const string FullSource = "FULL_SOURCE";
const string ExactTextV1 = "exact-text.v1";

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
    await AssertNoMissingExactEvidenceAsync(connection, tenantId, cancellationToken);

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

    const string sql = """
        select
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
        from source_system_events e
        inner join source_capture_payload_evidence p
            on p."TenantId" = e."TenantId"
            and p."SourceSystemEventId" = e."Id"
        where e."TenantId" = @tenantId
          and p."CoverageScope" = 'FULL_SOURCE'
          and p."StorageContract" = 'exact-text.v1'
        order by e."ReceivedAtUtc", e."Id"
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
        var exactPayload = reader.GetString(6);
        var headersJson = reader.GetString(7);
        var evidenceHash = reader.GetString(10);
        var actualHash = Sha256(exactPayload);
        if (!string.Equals(actualHash, evidenceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Exact payload evidence hash mismatch at exported row {rowCount + 1}.");

        ValidateCaptureEnvelope(headersJson, actualHash, rowCount + 1);

        var sourceSystem = reader.GetString(3);
        connectorTypes.Add(sourceSystem);
        var row = new ScoutJournalExportRow(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            sourceSystem,
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            exactPayload,
            headersJson,
            reader.GetDateTime(8),
            reader.GetDateTime(9));

        var json = JsonSerializer.Serialize(row);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await output.WriteAsync(bytes, cancellationToken);
        exportHash.AppendData(bytes);
        rowCount++;
    }

    await output.FlushAsync(cancellationToken);
    output.Flush(flushToDisk: true);
    var fileSha256 = Convert.ToHexString(exportHash.GetHashAndReset()).ToLowerInvariant();

    var manifestPath = outputPath + ".manifest.json";
    if (!options.Overwrite && File.Exists(manifestPath))
        throw new InvalidOperationException($"Export manifest already exists: {manifestPath}. Pass --overwrite to replace it.");

    var exportManifest = new ScoutJournalExportManifest(
        ExportContract,
        options.TenantSlug,
        DateTime.UtcNow,
        rowCount,
        fileSha256,
        ExactTextV1,
        connectorTypes.ToArray(),
        CustomerDataRemainsLocal: true,
        ContainsCredentialValues: false,
        ContainsProtectedCredentialReferences: false,
        ContainsExactCustomerPayloads: true,
        Purpose: "Customer-local Scout -> Fortress governed-state rebuild only. Do not upload this file to KynticAI Cloud.");

    await File.WriteAllTextAsync(
        manifestPath,
        JsonSerializer.Serialize(exportManifest, new JsonSerializerOptions { WriteIndented = true }),
        Encoding.UTF8,
        cancellationToken);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        contract = ExportContract,
        tenant = options.TenantSlug,
        rows = rowCount,
        sha256 = fileSha256,
        payloadStorageContract = ExactTextV1,
        connectorTypes,
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
    const string installationCountSql = "select count(*) from connector_installations where \"TenantId\" = @tenantId";
    await using (var installationCount = new NpgsqlCommand(installationCountSql, connection))
    {
        installationCount.Parameters.AddWithValue("tenantId", tenantId);
        var count = Convert.ToInt64(await installationCount.ExecuteScalarAsync(cancellationToken));
        if (count == 0)
            throw new InvalidOperationException("Scout has no connector installations; an automatic source-continuity export cannot be proven.");
    }

    const string unsafeCheckpointSql = """
        select count(*)
        from connector_installations i
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
          )
        """;
    await using var command = new NpgsqlCommand(unsafeCheckpointSql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    var unsafeCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    if (unsafeCount != 0)
    {
        throw new InvalidOperationException(
            $"{unsafeCount} connector installation(s) do not have a completed FULL_SOURCE exact-text.v1 generation. Run/repair Scout full-source capture before export.");
    }
}

static async Task AssertNoMissingExactEvidenceAsync(
    NpgsqlConnection connection,
    Guid tenantId,
    CancellationToken cancellationToken)
{
    // HeadersJson is semantic jsonb and is safe to use for metadata classification. The payload
    // itself is never recovered from jsonb for replay; exact text comes only from the sidecar.
    const string sql = """
        select count(*)
        from source_system_events e
        left join source_capture_payload_evidence p
          on p."TenantId" = e."TenantId"
         and p."SourceSystemEventId" = e."Id"
        where e."TenantId" = @tenantId
          and e."HeadersJson" -> 'kynticCapture' ->> 'Contract' = 'kyntic-local-source-capture.v1'
          and e."HeadersJson" -> 'kynticCapture' ->> 'CoverageScope' = 'FULL_SOURCE'
          and (
                p."Id" is null
             or p."StorageContract" <> 'exact-text.v1'
             or p."RawPayloadSha256" is null
          )
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("tenantId", tenantId);
    var missing = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    if (missing != 0)
    {
        throw new InvalidOperationException(
            $"{missing} retained FULL_SOURCE event(s) do not have exact customer-local payload evidence. Refusing a partial export.");
    }
}

static void ValidateCaptureEnvelope(string headersJson, string exactPayloadHash, long rowNumber)
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

sealed record ScoutJournalExportManifest(
    string Contract,
    string TenantSlug,
    DateTime GeneratedAtUtc,
    long Rows,
    string JournalSha256,
    string PayloadStorageContract,
    IReadOnlyList<string> ConnectorTypes,
    bool CustomerDataRemainsLocal,
    bool ContainsCredentialValues,
    bool ContainsProtectedCredentialReferences,
    bool ContainsExactCustomerPayloads,
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
