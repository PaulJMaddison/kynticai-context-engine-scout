using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KynticAI.Scout.Application;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KynticAI.Scout.MigrationTool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
        => (int)await ScoutMigrationToolProgram.RunAsync(args, Console.Out, Console.Error);
}

public static class ScoutMigrationToolProgram
{
    public static async Task<MigrationToolExitCode> RunAsync(
        string[] args,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        output ??= TextWriter.Null;
        error ??= TextWriter.Null;

        var parseResult = MigrationExportOptions.Parse(args);
        if (parseResult.ShowHelp)
        {
            await output.WriteLineAsync(MigrationExportOptions.Usage);
            return MigrationToolExitCode.Success;
        }

        if (parseResult.Options is null)
        {
            await error.WriteLineAsync(parseResult.ErrorMessage);
            await error.WriteLineAsync(MigrationExportOptions.Usage);
            return MigrationToolExitCode.UsageError;
        }

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            AddRepoConfiguration(builder.Configuration, builder.Environment.EnvironmentName, parseResult.Options.SettingsPath);
            builder.Services
                .AddScoutApplication()
                .AddScoutInfrastructure(builder.Configuration, builder.Environment);

            using var host = builder.Build();
            var runner = new MigrationExportRunner(host.Services, output, error);
            var result = await runner.RunAsync(parseResult.Options, cancellationToken);
            return result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Migration export was cancelled.");
            return MigrationToolExitCode.UnexpectedError;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"Migration export failed: {exception.Message}");
            return MigrationToolExitCode.UnexpectedError;
        }
    }

    private static void AddRepoConfiguration(IConfigurationBuilder configuration, string environmentName, string? settingsPath)
    {
        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
        var apiSettingsDirectory = Path.Combine(repoRoot, "src", "KynticAI.Scout.Api");
        configuration
            .AddJsonFile(Path.Combine(apiSettingsDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(apiSettingsDirectory, $"appsettings.{environmentName}.json"), optional: true, reloadOnChange: false);

        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            configuration.AddJsonFile(Path.GetFullPath(settingsPath), optional: false, reloadOnChange: false);
        }

        configuration.AddEnvironmentVariables();
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KynticAI.Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return startDirectory;
    }
}

public sealed record MigrationExportOptions(
    string TenantSlug,
    string OutputPath,
    StorageAdapterDataScope Scope,
    int MaxRecords,
    bool DryRun,
    string? Checkpoint,
    string? Provider,
    Guid? TenantId,
    string Purpose,
    string CorrelationId,
    string? SettingsPath)
{
    public const string PackageKind = "kynticai.scout.migration-export-package.v1";
    public const string ExportContractVersion = "kynticai.scout.storage-portable-export.v1";

    public static readonly StorageAdapterDataScope DefaultScope =
        StorageAdapterDataScope.TenantMetadata
        | StorageAdapterDataScope.SourceEvents
        | StorageAdapterDataScope.UserSignals
        | StorageAdapterDataScope.SelectorDefinitions
        | StorageAdapterDataScope.SelectorExecutions
        | StorageAdapterDataScope.ContextSnapshots
        | StorageAdapterDataScope.ContextFacts
        | StorageAdapterDataScope.Provenance
        | StorageAdapterDataScope.AuditEvents;

    public static string Usage => """
        Usage:
          dotnet run --project tools/KynticAI.Scout.MigrationTool -- export --tenant <tenant-slug> --out <local-folder> [options]

        Options:
          --dry-run                    Validate locally and write reports without export batch files.
          --scope <items>              Comma-separated scopes. Defaults to current Scout migration scopes.
                                       Supported aliases: all, relationship-inputs, tenant-metadata,
                                       source-events, user-signals, selectors, selector-executions,
                                       context-snapshots, context-facts, provenance, audit-events,
                                       data-items, relationship-sets, attribution-paths, outcome-events, vectors.
          --max-records <number>       Records per batch. Default: 500.
          --checkpoint <token>         Resume from an export checkpoint.
          --provider <key>             Storage adapter provider. Default: configured StorageAdapter:Provider.
          --tenant-id <guid>           Optional tenant ID guard; slug and ID must match.
          --purpose <text>             Request purpose metadata. Default: scout-open-core-migration-export.
          --correlation-id <id>        Request correlation ID. Default: generated locally.
          --settings <path>            Optional extra local appsettings JSON file.

        The tool writes only local files. It has no Cloud upload mode.
        """;

    public static MigrationExportOptionsParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            return new MigrationExportOptionsParseResult(null, null, ShowHelp: true);
        }

        if (!string.Equals(args[0], "export", StringComparison.OrdinalIgnoreCase))
        {
            return new MigrationExportOptionsParseResult(null, $"Unknown command '{args[0]}'. Use 'export'.");
        }

        string? tenantSlug = null;
        string? outputPath = null;
        var scope = DefaultScope;
        var maxRecords = 500;
        var dryRun = false;
        string? checkpoint = null;
        string? provider = null;
        Guid? tenantId = null;
        var purpose = "scout-open-core-migration-export";
        var correlationId = $"scout-migration-{Guid.NewGuid():N}";
        string? settingsPath = null;

        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            if (IsHelp(argument))
            {
                return new MigrationExportOptionsParseResult(null, null, ShowHelp: true);
            }

            if (string.Equals(argument, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                return new MigrationExportOptionsParseResult(null, $"Unexpected argument '{argument}'.");
            }

            var (name, valueFromEquals) = SplitOption(argument);
            var value = valueFromEquals ?? ReadOptionValue(args, ref index, name);
            if (value is null)
            {
                return new MigrationExportOptionsParseResult(null, $"Option '{name}' requires a value.");
            }

            switch (name.ToLowerInvariant())
            {
                case "--tenant":
                    tenantSlug = value.Trim();
                    break;
                case "--out":
                    outputPath = value.Trim();
                    break;
                case "--scope":
                    if (!TryParseScope(value, out scope, out var scopeError))
                    {
                        return new MigrationExportOptionsParseResult(null, scopeError);
                    }

                    break;
                case "--max-records":
                    if (!int.TryParse(value, out maxRecords) || maxRecords <= 0)
                    {
                        return new MigrationExportOptionsParseResult(null, "--max-records must be a positive integer.");
                    }

                    break;
                case "--checkpoint":
                    checkpoint = value.Trim();
                    break;
                case "--provider":
                    provider = value.Trim();
                    break;
                case "--tenant-id":
                    if (!Guid.TryParse(value, out var parsedTenantId))
                    {
                        return new MigrationExportOptionsParseResult(null, "--tenant-id must be a GUID.");
                    }

                    tenantId = parsedTenantId;
                    break;
                case "--purpose":
                    purpose = value.Trim();
                    break;
                case "--correlation-id":
                    correlationId = value.Trim();
                    break;
                case "--settings":
                    settingsPath = value.Trim();
                    break;
                case "--cloud-upload":
                case "--upload":
                    return new MigrationExportOptionsParseResult(null, "Cloud upload is not supported for Scout migration exports.");
                default:
                    return new MigrationExportOptionsParseResult(null, $"Unknown option '{name}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            return new MigrationExportOptionsParseResult(null, "--tenant is required.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new MigrationExportOptionsParseResult(null, "--out is required.");
        }

        if (scope == StorageAdapterDataScope.None)
        {
            return new MigrationExportOptionsParseResult(null, "At least one export scope is required.");
        }

        return new MigrationExportOptionsParseResult(new MigrationExportOptions(
            tenantSlug,
            outputPath,
            scope,
            maxRecords,
            dryRun,
            checkpoint,
            provider,
            tenantId,
            purpose,
            correlationId,
            settingsPath),
            ErrorMessage: null);
    }

    private static bool IsHelp(string argument)
        => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "help", StringComparison.OrdinalIgnoreCase);

    private static (string Name, string? Value) SplitOption(string argument)
    {
        var equalsIndex = argument.IndexOf('=', StringComparison.Ordinal);
        return equalsIndex < 0
            ? (argument, null)
            : (argument[..equalsIndex], argument[(equalsIndex + 1)..]);
    }

    private static string? ReadOptionValue(IReadOnlyList<string> args, ref int index, string optionName)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        index++;
        return args[index];
    }

    private static bool TryParseScope(string value, out StorageAdapterDataScope scope, out string? error)
    {
        scope = StorageAdapterDataScope.None;
        error = null;

        foreach (var rawItem in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var item = rawItem.Trim().ToLowerInvariant();
            scope |= item switch
            {
                "all" or "default" => DefaultScope,
                "relationship-inputs" => StorageAdapterDataScope.SourceEvents
                    | StorageAdapterDataScope.UserSignals
                    | StorageAdapterDataScope.SelectorDefinitions
                    | StorageAdapterDataScope.SelectorExecutions
                    | StorageAdapterDataScope.ContextSnapshots
                    | StorageAdapterDataScope.ContextFacts
                    | StorageAdapterDataScope.Provenance,
                "tenant-metadata" or "context-metadata" => StorageAdapterDataScope.TenantMetadata,
                "source-events" or "source-system-events" => StorageAdapterDataScope.SourceEvents,
                "user-signals" or "signals" => StorageAdapterDataScope.UserSignals,
                "selectors" or "selector-definitions" => StorageAdapterDataScope.SelectorDefinitions,
                "selector-executions" => StorageAdapterDataScope.SelectorExecutions,
                "context-snapshots" => StorageAdapterDataScope.ContextSnapshots,
                "context-facts" => StorageAdapterDataScope.ContextFacts,
                "provenance" => StorageAdapterDataScope.Provenance,
                "audit-events" or "audit" => StorageAdapterDataScope.AuditEvents,
                "data-items" => StorageAdapterDataScope.DataItems,
                "relationship-sets" => StorageAdapterDataScope.RelationshipSets,
                "attribution-paths" => StorageAdapterDataScope.AttributionPaths,
                "outcome-events" => StorageAdapterDataScope.OutcomeEvents,
                "vectors" => StorageAdapterDataScope.Vectors,
                _ => StorageAdapterDataScope.None
            };

            if ((scope & StorageAdapterDataScope.None) == StorageAdapterDataScope.None
                && !IsKnownScopeAlias(item))
            {
                error = $"Unknown export scope '{rawItem}'.";
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownScopeAlias(string item)
        => item is "all" or "default" or "relationship-inputs" or "tenant-metadata" or "context-metadata"
            or "source-events" or "source-system-events" or "user-signals" or "signals"
            or "selectors" or "selector-definitions" or "selector-executions" or "context-snapshots"
            or "context-facts" or "provenance" or "audit-events" or "audit" or "data-items"
            or "relationship-sets" or "attribution-paths" or "outcome-events" or "vectors";
}

public sealed record MigrationExportOptionsParseResult(
    MigrationExportOptions? Options,
    string? ErrorMessage,
    bool ShowHelp = false);

public enum MigrationToolExitCode
{
    Success = 0,
    UsageError = 1,
    ValidationFailed = 2,
    AdapterUnavailable = 3,
    IoFailure = 4,
    UnexpectedError = 5
}

public sealed record MigrationExportResult(
    MigrationToolExitCode ExitCode,
    string Message,
    string OutputPath,
    int BatchCount,
    int ExportedRecords);

public sealed class MigrationExportRunner(
    IServiceProvider serviceProvider,
    TextWriter? output = null,
    TextWriter? error = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly IReadOnlyList<string> ExcludedFilesAndFields =
    [
        "connector_credentials table",
        "saas_webhook_signing_secrets table",
        "saas_api_clients API key material",
        "data_sources.connection_config_json",
        "source_system_events.headers_json",
        "data-protection key-ring files",
        "local .env files",
        "licence/private key/certificate files",
        "Cloud upload or staging locations"
    ];

    private readonly TextWriter output = output ?? TextWriter.Null;
    private readonly TextWriter error = error ?? TextWriter.Null;

    public async Task<MigrationExportResult> RunAsync(
        MigrationExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var outputPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(outputPath);

        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IScoutDbContext>();
            var tenantContext = await ResolveTenantContextAsync(dbContext, options, outputPath, cancellationToken);
            if (tenantContext is null)
            {
                return new MigrationExportResult(
                    MigrationToolExitCode.ValidationFailed,
                    "Tenant validation failed.",
                    outputPath,
                    BatchCount: 0,
                    ExportedRecords: 0);
            }

            options = options with { TenantId = tenantContext.TenantId, TenantSlug = tenantContext.TenantSlug };
            var adapter = scope.ServiceProvider
                .GetRequiredService<ILocalDataPlaneStorageAdapterResolver>()
                .GetRequiredAdapter(options.Provider);
            var requestContext = CreateRequestContext(options, tenantContext);
            var capabilityResult = await ValidateAdapterAsync(adapter, requestContext, options, outputPath, cancellationToken);
            if (capabilityResult is not null)
            {
                return capabilityResult;
            }

            return await ExportAsync(adapter, requestContext, options, outputPath, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            await error.WriteLineAsync(exception.Message);
            await WriteToolValidationFilesAsync(
                outputPath,
                options,
                isValid: false,
                checkedRecords: 0,
                exportedRecords: 0,
                batchCount: 0,
                countsByKind: new Dictionary<string, int>(StringComparer.Ordinal),
                findings:
                [
                    new StorageMigrationValidationFinding(
                        StorageMigrationValidationSeverity.Error,
                        "adapter.unavailable",
                        exception.Message)
                ],
                errors:
                [
                    new ExtensionError(
                        ExtensionErrorCode.NotConfigured,
                        exception.Message,
                        options.Provider)
                ],
                batchFiles: [],
                nextCheckpoint: options.Checkpoint,
                isFinal: false,
                cancellationToken);
            return new MigrationExportResult(
                MigrationToolExitCode.AdapterUnavailable,
                exception.Message,
                outputPath,
                BatchCount: 0,
                ExportedRecords: 0);
        }
        catch (IOException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return new MigrationExportResult(
                MigrationToolExitCode.IoFailure,
                exception.Message,
                outputPath,
                BatchCount: 0,
                ExportedRecords: 0);
        }
    }

    private async Task<TenantContext?> ResolveTenantContextAsync(
        IScoutDbContext dbContext,
        MigrationExportOptions options,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var tenantSlug = options.TenantSlug.Trim().ToLowerInvariant();
        var tenant = options.TenantId.HasValue
            ? await dbContext.Tenants
                .AsNoTracking()
                .Where(candidate => candidate.Id == options.TenantId.Value)
                .OrderBy(candidate => candidate.Id)
                .SingleOrDefaultAsync(cancellationToken)
            : await dbContext.Tenants
                .AsNoTracking()
                .Where(candidate => candidate.Slug == tenantSlug)
                .OrderBy(candidate => candidate.Id)
                .SingleOrDefaultAsync(cancellationToken);

        if (tenant is null)
        {
            await WriteToolValidationFilesAsync(
                outputPath,
                options,
                isValid: false,
                checkedRecords: 0,
                exportedRecords: 0,
                batchCount: 0,
                countsByKind: new Dictionary<string, int>(StringComparer.Ordinal),
                findings:
                [
                    new StorageMigrationValidationFinding(
                        StorageMigrationValidationSeverity.Error,
                        "tenant.not_found",
                        "The requested tenant was not found in local Scout storage.",
                        Target: options.TenantId?.ToString("D") ?? options.TenantSlug)
                ],
                errors:
                [
                    new ExtensionError(
                        ExtensionErrorCode.NotFound,
                        "The requested tenant was not found in local Scout storage.",
                        options.TenantId?.ToString("D") ?? options.TenantSlug)
                ],
                batchFiles: [],
                nextCheckpoint: options.Checkpoint,
                isFinal: true,
                cancellationToken);
            return null;
        }

        return new TenantContext(
            tenant.Id,
            tenant.Slug,
            EnvironmentKey: "local",
            CorrelationId: options.CorrelationId);
    }

    private async Task<MigrationExportResult?> ValidateAdapterAsync(
        ILocalDataPlaneStorageAdapter adapter,
        StorageAdapterRequestContext requestContext,
        MigrationExportOptions options,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var capabilities = await adapter.GetCapabilitiesAsync(
            new StorageAdapterCapabilitiesRequest(requestContext),
            cancellationToken);
        if (!capabilities.SupportsExport || capabilities.UsesCloudDataPlane)
        {
            var message = capabilities.UsesCloudDataPlane
                ? "Configured storage adapter reports Cloud data-plane use, which is not allowed for Scout migration export."
                : "Configured storage adapter does not support export.";
            await WriteToolValidationFilesAsync(
                outputPath,
                options,
                isValid: false,
                checkedRecords: 0,
                exportedRecords: 0,
                batchCount: 0,
                countsByKind: new Dictionary<string, int>(StringComparer.Ordinal),
                findings:
                [
                    new StorageMigrationValidationFinding(
                        StorageMigrationValidationSeverity.Error,
                        capabilities.UsesCloudDataPlane ? "cloud_data_plane.blocked" : "adapter.export_not_supported",
                        message,
                        Target: capabilities.AdapterKey)
                ],
                errors:
                [
                    new ExtensionError(
                        capabilities.UsesCloudDataPlane ? ExtensionErrorCode.UnsafeOperation : ExtensionErrorCode.NotSupported,
                        message,
                        capabilities.AdapterKey)
                ],
                batchFiles: [],
                nextCheckpoint: options.Checkpoint,
                isFinal: false,
                cancellationToken);
            return new MigrationExportResult(MigrationToolExitCode.ValidationFailed, message, outputPath, 0, 0);
        }

        var health = await adapter.CheckHealthAsync(new StorageAdapterHealthRequest(requestContext), cancellationToken);
        if (health.Readiness is StorageAdapterReadiness.Disabled or StorageAdapterReadiness.Unavailable
            || JsonBoolean(health.Diagnostics["usesCloudDataPlane"]))
        {
            var message = JsonBoolean(health.Diagnostics["usesCloudDataPlane"])
                ? "Storage adapter health diagnostics reported Cloud data-plane use, which is not allowed."
                : health.Status;
            await WriteToolValidationFilesAsync(
                outputPath,
                options,
                isValid: false,
                checkedRecords: 0,
                exportedRecords: 0,
                batchCount: 0,
                countsByKind: new Dictionary<string, int>(StringComparer.Ordinal),
                findings:
                [
                    new StorageMigrationValidationFinding(
                        StorageMigrationValidationSeverity.Error,
                        JsonBoolean(health.Diagnostics["usesCloudDataPlane"]) ? "cloud_data_plane.blocked" : "adapter.health_unavailable",
                        message,
                        Target: health.AdapterKey)
                ],
                errors: health.Errors.Count == 0
                    ? [new ExtensionError(ExtensionErrorCode.ExternalDependencyFailed, message, health.AdapterKey)]
                    : health.Errors,
                batchFiles: [],
                nextCheckpoint: options.Checkpoint,
                isFinal: false,
                cancellationToken);
            return new MigrationExportResult(MigrationToolExitCode.AdapterUnavailable, message, outputPath, 0, 0);
        }

        return null;
    }

    private async Task<MigrationExportResult> ExportAsync(
        ILocalDataPlaneStorageAdapter adapter,
        StorageAdapterRequestContext requestContext,
        MigrationExportOptions options,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var findings = new List<StorageMigrationValidationFinding>();
        var errors = new List<ExtensionError>();
        var countsByKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var batchFiles = new List<string>();
        string? checkpoint = options.Checkpoint;
        var isFinal = false;
        var checkedRecords = 0;
        var exportedRecords = 0;
        var batchCount = 0;

        do
        {
            var request = new StorageExportRequest(
                requestContext,
                options.Scope,
                Checkpoint: checkpoint,
                MaxRecords: options.MaxRecords,
                QuietMigrationMode: true,
                DryRun: options.DryRun);
            var yieldedBatch = false;

            await foreach (var batch in adapter.ExportAsync(request, cancellationToken))
            {
                yieldedBatch = true;
                batchCount++;
                isFinal = batch.IsFinal;
                checkpoint = batch.NextCheckpoint;
                errors.AddRange(batch.Errors);

                if (batch.ValidationReport is not null)
                {
                    checkedRecords = Math.Max(checkedRecords, batch.ValidationReport.CheckedRecords);
                    findings.AddRange(batch.ValidationReport.Findings);
                    foreach (var (kind, count) in batch.ValidationReport.CountsByRecordKind)
                    {
                        countsByKind[kind] = count;
                    }
                }

                if (JsonBoolean(batch.Diagnostics["usesCloudDataPlane"]))
                {
                    findings.Add(new StorageMigrationValidationFinding(
                        StorageMigrationValidationSeverity.Error,
                        "cloud_data_plane.blocked",
                        "The export batch reported Cloud data-plane use, which is not allowed.",
                        Target: batch.BatchId));
                    errors.Add(new ExtensionError(
                        ExtensionErrorCode.UnsafeOperation,
                        "The export batch reported Cloud data-plane use, which is not allowed.",
                        batch.BatchId));
                }

                var batchValid = batch.ValidationReport?.IsValid != false
                    && batch.Errors.Count == 0
                    && findings.All(static finding => finding.Severity != StorageMigrationValidationSeverity.Error);
                if (!batchValid)
                {
                    await WriteToolValidationFilesAsync(
                        outputPath,
                        options,
                        isValid: false,
                        checkedRecords,
                        exportedRecords: 0,
                        batchCount: options.DryRun ? batchCount : Math.Max(0, batchCount - 1),
                        countsByKind,
                        findings,
                        errors,
                        batchFiles,
                        checkpoint,
                        isFinal,
                        cancellationToken);
                    await error.WriteLineAsync("Migration export validation failed. No export batch file was written for the failing batch.");
                    return new MigrationExportResult(
                        MigrationToolExitCode.ValidationFailed,
                        "Migration export validation failed.",
                        outputPath,
                        batchFiles.Count,
                        batchFiles.Count == 0 ? 0 : exportedRecords);
                }

                if (!options.DryRun)
                {
                    var batchDirectory = Path.Combine(outputPath, "batches");
                    Directory.CreateDirectory(batchDirectory);
                    var batchFileName = $"batch-{batchCount:000000}.json";
                    var batchPath = Path.Combine(batchDirectory, batchFileName);
                    await WriteJsonAsync(batchPath, batch, cancellationToken);
                    batchFiles.Add(Path.Combine("batches", batchFileName).Replace('\\', '/'));
                    exportedRecords += batch.Records.Count;
                }

                if (options.DryRun)
                {
                    isFinal = true;
                    checkpoint = null;
                    break;
                }
            }

            if (!yieldedBatch)
            {
                findings.Add(new StorageMigrationValidationFinding(
                    StorageMigrationValidationSeverity.Error,
                    "adapter.no_batches",
                    "The storage adapter did not return an export batch."));
                errors.Add(new ExtensionError(
                    ExtensionErrorCode.ExternalDependencyFailed,
                    "The storage adapter did not return an export batch.",
                    adapter.AdapterKey));
                isFinal = true;
            }
        }
        while (!options.DryRun && !isFinal);

        var isValid = errors.Count == 0
            && findings.All(static finding => finding.Severity != StorageMigrationValidationSeverity.Error);
        await WriteToolValidationFilesAsync(
            outputPath,
            options,
            isValid,
            checkedRecords,
            exportedRecords,
            batchCount,
            countsByKind,
            findings,
            errors,
            batchFiles,
            checkpoint,
            isFinal,
            cancellationToken);

        var message = options.DryRun
            ? "Migration export dry run completed."
            : $"Migration export package completed with {batchFiles.Count} batch file(s).";
        await output.WriteLineAsync($"{message} Output: {outputPath}");
        return new MigrationExportResult(
            isValid ? MigrationToolExitCode.Success : MigrationToolExitCode.ValidationFailed,
            message,
            outputPath,
            batchFiles.Count,
            exportedRecords);
    }

    private static StorageAdapterRequestContext CreateRequestContext(MigrationExportOptions options, TenantContext tenantContext)
        => new(
            tenantContext,
            options.Purpose,
            options.CorrelationId,
            Actor: new EnterpriseActorContext(
                "scout-migration-tool",
                "KynticAI Scout migration tool",
                null,
                ["operator"]),
            Metadata: new JsonObject
            {
                ["tool"] = "KynticAI.Scout.MigrationTool",
                ["packageKind"] = MigrationExportOptions.PackageKind,
                ["dryRun"] = options.DryRun,
                ["cloudUploadSupported"] = false
            });

    private async Task WriteToolValidationFilesAsync(
        string outputPath,
        MigrationExportOptions options,
        bool isValid,
        int checkedRecords,
        int exportedRecords,
        int batchCount,
        IReadOnlyDictionary<string, int> countsByKind,
        IReadOnlyList<StorageMigrationValidationFinding> findings,
        IReadOnlyList<ExtensionError> errors,
        IReadOnlyList<string> batchFiles,
        string? nextCheckpoint,
        bool isFinal,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var manifest = new JsonObject
        {
            ["packageKind"] = MigrationExportOptions.PackageKind,
            ["contractVersion"] = MigrationExportOptions.ExportContractVersion,
            ["generatedAtUtc"] = FormatUtc(generatedAtUtc),
            ["dryRun"] = options.DryRun,
            ["tenantSlug"] = options.TenantSlug,
            ["tenantId"] = options.TenantId?.ToString("D"),
            ["scope"] = options.Scope.ToString(),
            ["maxRecords"] = options.MaxRecords,
            ["provider"] = options.Provider,
            ["purpose"] = options.Purpose,
            ["correlationId"] = options.CorrelationId,
            ["isFinal"] = isFinal,
            ["nextCheckpoint"] = nextCheckpoint,
            ["batchCount"] = batchCount,
            ["exportedRecords"] = exportedRecords,
            ["usesCloudDataPlane"] = false,
            ["files"] = ToJsonArray(new[] { "manifest.json", "validation-report.json" }.Concat(batchFiles)),
            ["excludedFilesAndFields"] = ToJsonArray(ExcludedFilesAndFields)
        };
        var validationReport = new JsonObject
        {
            ["packageKind"] = MigrationExportOptions.PackageKind,
            ["contractVersion"] = MigrationExportOptions.ExportContractVersion,
            ["generatedAtUtc"] = FormatUtc(generatedAtUtc),
            ["dryRun"] = options.DryRun,
            ["isValid"] = isValid,
            ["tenantSlug"] = options.TenantSlug,
            ["tenantId"] = options.TenantId?.ToString("D"),
            ["scope"] = options.Scope.ToString(),
            ["checkedRecords"] = checkedRecords,
            ["exportedRecords"] = exportedRecords,
            ["batchCount"] = batchCount,
            ["countsByRecordKind"] = CountsToJsonObject(countsByKind),
            ["findings"] = ToJsonArray(findings),
            ["errors"] = ToJsonArray(errors),
            ["excludedFilesAndFields"] = ToJsonArray(ExcludedFilesAndFields),
            ["usesCloudDataPlane"] = false,
            ["cloudUploadSupported"] = false
        };

        await WriteJsonAsync(Path.Combine(outputPath, "manifest.json"), manifest, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputPath, "validation-report.json"), validationReport, cancellationToken);
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static JsonArray ToJsonArray<T>(IEnumerable<T> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(JsonSerializer.SerializeToNode(value, JsonOptions));
        }

        return array;
    }

    private static JsonObject CountsToJsonObject(IReadOnlyDictionary<string, int> counts)
    {
        var result = new JsonObject();
        foreach (var (key, value) in counts.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            result[key] = value;
        }

        return result;
    }

    private static bool JsonBoolean(JsonNode? node)
        => node is not null
            && node.GetValueKind() == JsonValueKind.True
            && node.GetValue<bool>();

    private static string FormatUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value.ToString("O")
            : DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O");
}
