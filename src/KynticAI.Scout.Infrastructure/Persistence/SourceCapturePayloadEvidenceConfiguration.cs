using KynticAI.Scout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KynticAI.Scout.Infrastructure.Persistence;

internal sealed class SourceCapturePayloadEvidenceConfiguration
    : IEntityTypeConfiguration<SourceCapturePayloadEvidence>
{
    public void Configure(EntityTypeBuilder<SourceCapturePayloadEvidence> builder)
    {
        builder.ToTable("source_capture_payload_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StorageContract).HasMaxLength(80).IsRequired();
        builder.Property(x => x.CoverageScope).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ExactPayloadText).HasColumnType("text").IsRequired();
        builder.Property(x => x.RawPayloadSha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.SourceSystemEventId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ConnectorInstallationId });
        builder.HasOne(x => x.SourceSystemEvent)
            .WithOne()
            .HasForeignKey<SourceCapturePayloadEvidence>(x => x.SourceSystemEventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
