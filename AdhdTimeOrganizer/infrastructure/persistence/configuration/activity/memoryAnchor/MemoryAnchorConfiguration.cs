using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity.memoryAnchor;

public class MemoryAnchorConfiguration : IEntityTypeConfiguration<MemoryAnchor>
{
    public void Configure(EntityTypeBuilder<MemoryAnchor> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser(u => u.MemoryAnchors);
        builder.IsManyWithOneActivity(a => a.MemoryAnchors);

        builder.Property(a => a.AnchorMonth).IsRequired();
        builder.Property(a => a.AnchorYear).IsRequired();
        builder.Property(a => a.HighlightNote).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.Rating).IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_memory_anchor_month", "anchor_month BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_memory_anchor_rating", "rating BETWEEN 1 AND 10");
        });

        builder.HasIndex(a => new { a.ActivityId, a.AnchorYear, a.AnchorMonth });
    }
}
