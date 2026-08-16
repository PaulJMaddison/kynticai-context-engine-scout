using System;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KynticAI.Scout.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ScoutDbContext))]
[Migration("20260816115800_ConnectorCaptureOwnership")]
public sealed class ConnectorCaptureOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "connector_capture_ownership",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ConnectorInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                SelectedGeneration = table.Column<long>(type: "bigint", nullable: false),
                SnapshotCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                HighWaterMarkSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CutoverEpoch = table.Column<Guid>(type: "uuid", nullable: false),
                CutoverTokenSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ScoutPausedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                FortressOwnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_connector_capture_ownership", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_connector_capture_ownership_TenantId_ConnectorInstallationId",
            table: "connector_capture_ownership",
            columns: new[] { "TenantId", "ConnectorInstallationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_connector_capture_ownership_TenantId_State",
            table: "connector_capture_ownership",
            columns: new[] { "TenantId", "State" });

        migrationBuilder.CreateIndex(
            name: "IX_connector_capture_ownership_TenantId_CutoverEpoch",
            table: "connector_capture_ownership",
            columns: new[] { "TenantId", "CutoverEpoch" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "connector_capture_ownership");
    }
}
