using KynticAI.Scout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KynticAI.Scout.Infrastructure.Persistence;

internal sealed class ConnectorCaptureOwnershipConfiguration
    : IEntityTypeConfiguration<ConnectorCaptureOwnership>
{
    public void Configure(EntityTypeBuilder<ConnectorCaptureOwnership> builder)
    {
        builder.ToTable("connector_capture_ownership");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.State)
            .HasConversion<string>()
            .HasMaxLength(80)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(x => x.SelectedGeneration).IsRequired();
        builder.Property(x => x.SnapshotCompletedAtUtc).IsRequired();
        builder.Property(x => x.HighWaterMarkSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CutoverEpoch).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.CutoverTokenSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ScoutPausedAtUtc);
        builder.Property(x => x.FortressOwnedAtUtc);

        builder.HasIndex(x => new { x.TenantId, x.ConnectorInstallationId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.State });
        builder.HasIndex(x => new { x.TenantId, x.CutoverEpoch });
    }
}
