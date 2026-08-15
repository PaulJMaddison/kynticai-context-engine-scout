using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;

namespace KynticAI.Scout.Application.Services;

/// <summary>
/// Builds the public upgrade-compatible metadata embedded beside a Scout source event.
/// RawPayloadJson remains customer-local and is not duplicated into the metadata block.
/// </summary>
public static class LocalSourceCaptureEnvelope
{
    public static LocalSourceCaptureMetadataV1 FromConnectorResult(
        string connectorType,
        ConnectorCaptureMetadata metadata,
        DateTime ingestedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorType);
        ArgumentNullException.ThrowIfNull(metadata);
        if (ingestedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Ingested timestamp must be UTC.", nameof(ingestedAtUtc));

        var result = new LocalSourceCaptureMetadataV1(
            LocalDataPlaneContracts.CaptureMetadataV1,
            metadata.ConnectorInstanceId,
            connectorType.Trim(),
            metadata.ConnectorDefinitionVersion.Trim(),
            metadata.CaptureProfile.Trim(),
            metadata.CaptureProfileVersion.Trim(),
            string.IsNullOrWhiteSpace(metadata.SourceNamespace) ? null : metadata.SourceNamespace.Trim(),
            metadata.SourceObjectType.Trim(),
            metadata.SourceRecordId.Trim(),
            metadata.Operation.Trim(),
            metadata.SourcePositionJson.Trim(),
            EnsureUtc(metadata.OccurredAtUtc, nameof(metadata.OccurredAtUtc)),
            metadata.SourceRecordedAtUtc is null
                ? null
                : EnsureUtc(metadata.SourceRecordedAtUtc.Value, nameof(metadata.SourceRecordedAtUtc)),
            ingestedAtUtc,
            metadata.SchemaFingerprintSha256.Trim().ToLowerInvariant(),
            metadata.RedactionPolicyVersion.Trim(),
            metadata.FullPermittedPayloadRetained,
            metadata.IdempotencyKey.Trim());

        if (!result.IsUpgradeCompatible)
            throw new InvalidOperationException("Connector capture metadata is incomplete for upgrade-compatible capture.");
        return result;
    }

    public static string MergeIntoHeadersJson(string? existingHeadersJson, LocalSourceCaptureMetadataV1 capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!capture.IsUpgradeCompatible)
            throw new InvalidOperationException("Capture metadata is not upgrade compatible.");

        var root = string.IsNullOrWhiteSpace(existingHeadersJson)
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(existingHeadersJson)
                ?? new Dictionary<string, object?>();
        root["kynticCapture"] = capture;
        return JsonSerializer.Serialize(root);
    }

    public static string ConfigurationFingerprint(string sanitizedConfigurationJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sanitizedConfigurationJson ?? "{}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Capture timestamps must be UTC.", parameterName);
        return value;
    }
}
