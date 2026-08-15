using System.Text.Json;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Application.Contracts;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Domain.Entities;

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
    public void FreshPostgresWithNoCapture_IsHistoryLimitedNotLossless()
    {
        var readiness = ScoutFortressUpgradePolicy.Classify(new UpgradeCompatibilityEvidence(
            SupportedRelationalProvider: true,
            IsPostgres: true,
            HasConnectorInstallations: true,
            ConnectorCredentialsReferencedLocally: true,
            HasRetainedEvents: false,
            AllRetainedEventsHaveCaptureMetadata: false,
            AllRetainedEventsRetainFullPermittedPayload: false,
            ConnectorTypesSupportedByTarget: true,
            RequiresSourceReconnect: false,
            HistoricalCoverageKnownComplete: false));

        Assert.Equal(LocalUpgradeReadiness.HistoryLimited, readiness);
    }

    [Fact]
    public void ProvenEmptyPostgresSource_CanBeLosslessDerivedRebuild()
    {
        var readiness = ScoutFortressUpgradePolicy.Classify(new UpgradeCompatibilityEvidence(
            SupportedRelationalProvider: true,
            IsPostgres: true,
            HasConnectorInstallations: true,
            ConnectorCredentialsReferencedLocally: true,
            HasRetainedEvents: false,
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
    public void SubjectOnDemandCapture_IsNotMisrepresentedAsWholeSourceCoverage()
    {
        var now = DateTime.SpecifyKind(new DateTime(2026, 8, 15, 18, 30, 0), DateTimeKind.Utc);
        var capture = new LocalSourceCaptureMetadataV1(
            LocalDataPlaneContracts.CaptureMetadataV1,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "sqlDatabase",
            "sql.subject-fetch.v1",
            LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
            "1",
            "public",
            "contacts",
            "contact-42",
            "snapshot",
            "{\"observedAt\":\"2026-08-15T18:28:00Z\"}",
            now.AddMinutes(-2),
            now.AddMinutes(-2),
            now,
            new string('a', 64),
            "customer-permitted.v1",
            true,
            "capture-42",
            LocalDataPlaneContracts.CoverageSubjectOnDemand,
            LocalDataPlaneContracts.HistoryOnDemand,
            null,
            new string('b', 64),
            new string('c', 64));

        Assert.True(capture.HasStructurallyValidCaptureMetadata);
        Assert.False(capture.IsUpgradeCompatible);
    }

    [Fact]
    public void CheckpointKeepsLastNonEmptyPageHistoryAcrossEmptyTerminalPage()
    {
        var now = DateTime.SpecifyKind(new DateTime(2026, 8, 15, 19, 0, 0), DateTimeKind.Utc);
        var checkpoint = ConnectorCaptureCheckpoint.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            LocalDataPlaneContracts.CaptureProfileFullPermittedV1,
            "1",
            LocalDataPlaneContracts.CoverageFullSource,
            LocalDataPlaneContracts.HistoryUnknown,
            null,
            now);

        Assert.True(checkpoint.TryAcquireLease("test-owner", TimeSpan.FromMinutes(5), now));
        checkpoint.ObserveCaptureSemantics(
            "test-owner",
            LocalDataPlaneContracts.HistorySnapshotOnly,
            now.AddDays(-7),
            now.AddSeconds(1));
        checkpoint.Advance(
            "test-owner",
            "next-page",
            "{\"page\":1}",
            100,
            now.AddDays(-7),
            now,
            now.AddSeconds(2));

        // The final page is empty. Completion must use the prior non-empty page semantics rather
        // than silently reverting history to UNKNOWN.
        checkpoint.Advance(
            "test-owner",
            null,
            "{\"page\":2}",
            0,
            null,
            null,
            now.AddSeconds(3));
        checkpoint.CompleteFullSourceGeneration(
            "test-owner",
            "{\"page\":2}",
            checkpoint.HistoryCompleteness,
            now.AddSeconds(4));

        Assert.Equal(LocalDataPlaneContracts.HistorySnapshotOnly, checkpoint.HistoryCompleteness);
        Assert.Equal(now.AddDays(-7), checkpoint.EarliestAvailableAtUtc);
        Assert.Equal(1, checkpoint.Generation);
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
            "sql:800:3",
            CoverageScope: LocalDataPlaneContracts.CoverageFullSource,
            HistoryCompleteness: LocalDataPlaneContracts.HistoryFromRetentionBoundary,
            EarliestAvailableAtUtc: now.AddDays(-30),
            RawPayloadSha256: new string('b', 64),
            PermittedFieldSetSha256: new string('c', 64));

        var capture = LocalSourceCaptureEnvelope.FromConnectorResult("sql", metadata, now);
        var headers = LocalSourceCaptureEnvelope.MergeIntoHeadersJson("{\"trace\":\"safe\"}", capture);
        using var json = JsonDocument.Parse(headers);

        Assert.Equal(LocalDataPlaneContracts.CaptureMetadataV1,
            json.RootElement.GetProperty("kynticCapture").GetProperty("Contract").GetString());
        Assert.Contains("\"ordinal\":3", capture.SourcePositionJson, StringComparison.Ordinal);
        Assert.Equal(LocalDataPlaneContracts.CoverageFullSource, capture.CoverageScope);
        Assert.DoesNotContain("password", headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedValue", headers, StringComparison.OrdinalIgnoreCase);
    }
}
