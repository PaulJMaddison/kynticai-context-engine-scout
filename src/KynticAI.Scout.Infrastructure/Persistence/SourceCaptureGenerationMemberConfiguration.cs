using KynticAI.Scout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KynticAI.Scout.Infrastructure.Persistence;

internal sealed class SourceCaptureGenerationMemberConfiguration
    : IEntityTypeConfiguration<SourceCaptureGenerationMember>
{
    public void Configure(EntityTypeBuilder<SourceCaptureGenerationMember> builder)
    {
        builder.ToTable("source_capture_generation_members");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceNamespace).HasMaxLength(160).IsRequired();
        builder.Property(x => x.SourceObjectType).HasMaxLength(160).IsRequired();
        builder.Property(x => x.SourceRecordId).HasMaxLength(512).IsRequired();
        builder.HasIndex(x => new
        {
            x.TenantId,
            x.ConnectorInstallationId,
            x.Generation,
            x.SourceObjectType,
            x.SourceRecordId
        }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.SourceSystemEventId });
        builder.HasIndex(x => new { x.TenantId, x.ConnectorInstallationId, x.Generation });
        builder.HasOne(x => x.SourceSystemEvent)
            .WithMany()
            .HasForeignKey(x => x.SourceSystemEventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
