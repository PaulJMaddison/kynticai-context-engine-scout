using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KynticAI.Scout.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persists customer-local whole-source capture cursor/lease state and byte-preserving payload
/// evidence used by the Scout -> Fortress additive upgrade barrier. This migration is
/// deliberately self-contained because the branch has not been declared runtime-green or
/// deployed; the model snapshot must still be regenerated/reconciled by the normal EF tooling
/// before merge.
/// </summary>
[DbContext(typeof(ScoutDbContext))]
[Migration("20260815221500_ConnectorCaptureCheckpoints")]
public sealed class ConnectorCaptureCheckpoints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "connector_capture_checkpoints",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ConnectorInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                DataSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                CaptureProfile = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                CaptureProfileVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                CoverageScope = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                HistoryCompleteness = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                PayloadStorageContract = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "legacy-jsonb.v0"),
                ContinuationToken = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                HighWaterMarkJson = table.Column<string>(type: "jsonb", nullable: false),
                EarliestAvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EarliestCapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LatestCapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastFullSourceCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CapturedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                Generation = table.Column<long>(type: "bigint", nullable: false),
                LeaseOwner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_connector_capture_checkpoints", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "source_capture_payload_evidence",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceSystemEventId = table.Column<Guid>(type: "uuid", nullable: false),
                ConnectorInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                StorageContract = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                CoverageScope = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                CaptureGeneration = table.Column<long>(type: "bigint", nullable: false),
                ExactPayloadText = table.Column<string>(type: "text", nullable: false),
                RawPayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_source_capture_payload_evidence", x => x.Id);
                table.ForeignKey(
                    name: "FK_source_capture_payload_evidence_source_system_events_SourceSystemEventId",
                    column: x => x.SourceSystemEventId,
                    principalTable: "source_system_events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_connector_capture_checkpoints_TenantId_ConnectorInstallationId",
            table: "connector_capture_checkpoints",
            columns: new[] { "TenantId", "ConnectorInstallationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_connector_capture_checkpoints_TenantId_LastFullSourceCompletedAtUtc",
            table: "connector_capture_checkpoints",
            columns: new[] { "TenantId", "LastFullSourceCompletedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_connector_capture_checkpoints_TenantId_LeaseExpiresAtUtc",
            table: "connector_capture_checkpoints",
            columns: new[] { "TenantId", "LeaseExpiresAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_source_capture_payload_evidence_TenantId_SourceSystemEventId",
            table: "source_capture_payload_evidence",
            columns: new[] { "TenantId", "SourceSystemEventId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_source_capture_payload_evidence_TenantId_ConnectorInstallationId_CaptureGeneration",
            table: "source_capture_payload_evidence",
            columns: new[] { "TenantId", "ConnectorInstallationId", "CaptureGeneration" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "source_capture_payload_evidence");
        migrationBuilder.DropTable(name: "connector_capture_checkpoints");
    }
}
