using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;

namespace KynticAI.Scout.UnitTests;

public sealed class ScoutFortressUpgradeCompatibilityTests
{
    [Fact]
    public void CompletePostgresCapture_IsLosslessDerivedRebuild()
    {
        var readiness = ScoutFortressUpgradePolicy.Classify(new UpgradeCompatibilityEvidence(
            SupportedRelationalProvider: true,
            IsPostgres: true,
            HasConnectorInstallations: true,
            ConnectorCredentialsReferencedLocally: true,
            HasRetainedEvents: true,
            AllRetainedEventsHaveCaptureMetadata: true,
            AllRetainedEventsRetainFullPermittedPayload: true,
            ConnectorTypesSupportedByTarget: true,
            RequiresSourceReconnect: false,
            HistoricalCoverageKnownComplete: true));

        Assert.Equal(LocalUpgradeReadiness.LosslessDerivedRebuild, readiness);
    }

    [Fact]
    public void LegacyEventsWithoutCaptureMetadata_AreHistoryLimited()
    {
        var readiness = ScoutFortressUpgradePolicy.Classify(new UpgradeCompatibilityEvidence(
            true, true, true, true, true,
            AllRetainedEventsHaveCaptureMetadata: false,
            AllRetainedEventsRetainFullPermittedPayload: true,
            ConnectorTypesSupportedByTarget: true,
            RequiresSourceReconnect: false,
            HistoricalCoverageKnownComplete: false));

        Assert.Equal(LocalUpgradeReadiness.HistoryLimited, readiness);
    }

    [Fact]
    public void MissingLocalCredentialReference_RequiresReconnect()
    {
        var readiness = ScoutFortressUpgradePolicy.Classify(new UpgradeCompatibilityEvidence(
            true, true, true,
            ConnectorCredentialsReferencedLocally: false,
            HasRetainedEvents: true,
            AllRetainedEventsHaveCaptureMetadata: true,
            AllRetainedEventsRetainFullPermittedPayload: true,
            ConnectorTypesSupportedByTarget: true,
            RequiresSourceReconnect: false,
            HistoricalCoverageKnownComplete: true));

        Assert.Equal(LocalUpgradeReadiness.ReconnectRequired, readiness);
    }

    [Fact]
    public void UnsupportedConnector_FailsClosed()
    {
        var readiness = ScoutFortressUpgradePolicy.Classify(new UpgradeCompatibilityEvidence(
            true, true, true, true, true, true, true,
            ConnectorTypesSupportedByTarget: false,
            RequiresSourceReconnect: false,
            HistoricalCoverageKnownComplete: true));

        Assert.Equal(LocalUpgradeReadiness.Unsupported, readiness);
    }

    [Fact]
    public void CaptureEnvelope_PreservesExactSourcePositionWithoutSecrets()
    {
        var now = DateTime.SpecifyKind(new DateTime(2026, 8, 15, 18, 30, 0), DateTimeKind.Utc);
        var metadata = new ConnectorCaptureMetadata(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "sql.v1",
            LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
            "1",
            "public",
            "orders",
            "order-42",
            "update",
            "{\"lsn\":800,\"ordinal\":3}",
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            new string('a', 64),
            "redaction-v1",
            FullPermittedPayloadRetained: true,
            "sql:800:3");

        var capture = LocalSourceCaptureEnvelope.FromConnectorResult("sql", metadata, now);
        var headers = LocalSourceCaptureEnvelope.MergeIntoHeadersJson("{\"trace\":\"safe\"}", capture);
        using var json = JsonDocument.Parse(headers);

        Assert.Equal(LocalDataPlaneContracts.CaptureMetadataV1,
            json.RootElement.GetProperty("kynticCapture").GetProperty("Contract").GetString());
        Assert.Contains("\"ordinal\":3", capture.SourcePositionJson, StringComparison.Ordinal);
        Assert.DoesNotContain("password", headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedValue", headers, StringComparison.OrdinalIgnoreCase);
    }
}
