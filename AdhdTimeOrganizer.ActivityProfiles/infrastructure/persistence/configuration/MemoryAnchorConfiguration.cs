using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.configuration;

public class MemoryAnchorConfiguration : IEntityTypeConfiguration<MemoryAnchor>
{
    public void Configure(EntityTypeBuilder<MemoryAnchor> builder)
    {
        builder.BaseEntityConfigure();
        // Parameterless on purpose: User.MemoryAnchors and Activity.MemoryAnchors were deleted when
        // this entity left Core, so there is no inverse navigation to name. Both FKs are still
        // configured, still required and still cascade — see the comments on User / Activity.
        builder.IsManyWithOneUser();
        // Constraint name pinned for the same reason as the three profile configurations: dropping
        // Activity.MemoryAnchors changes the name EF derives.
        builder.IsManyWithOneActivity()
            .HasConstraintName("fk_memory_anchor_activities_activity_id");

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