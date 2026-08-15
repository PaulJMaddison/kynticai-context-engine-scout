using KynticAI.Scout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KynticAI.Scout.Infrastructure.Persistence;

internal sealed class ConnectorCaptureCheckpointConfiguration : IEntityTypeConfiguration<ConnectorCaptureCheckpoint>
{
    public void Configure(EntityTypeBuilder<ConnectorCaptureCheckpoint> builder)
    {
        builder.ToTable("connector_capture_checkpoints");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CaptureProfile).HasMaxLength(120).IsRequired();
        builder.Property(x => x.CaptureProfileVersion).HasMaxLength(80).IsRequired();
        builder.Property(x => x.CoverageScope).HasMaxLength(80).IsRequired();
        builder.Property(x => x.HistoryCompleteness).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ContinuationToken).HasMaxLength(8_000);
        builder.Property(x => x.HighWaterMarkJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.LeaseOwner).HasMaxLength(200);
        builder.Property(x => x.LastError).HasMaxLength(4_000);
        builder.HasIndex(x => new { x.TenantId, x.ConnectorInstallationId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.LeaseExpiresAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.LastFullSourceCompletedAtUtc });
    }
}
