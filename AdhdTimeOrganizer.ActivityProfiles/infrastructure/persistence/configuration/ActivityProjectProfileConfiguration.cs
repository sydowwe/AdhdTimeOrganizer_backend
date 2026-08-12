using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.configuration;

public class ActivityProjectProfileConfiguration : IEntityTypeConfiguration<ActivityProjectProfile>
{
    public void Configure(EntityTypeBuilder<ActivityProjectProfile> builder)
    {
        builder.BaseEntityConfigure();

        builder.HasOne(p => p.Activity)
            .WithOne()
            .HasForeignKey<ActivityProjectProfile>(p => p.ActivityId)
            .OnDelete(DeleteBehavior.Cascade)
            // Pinned — see ActivityBacklogProfileConfiguration.
            .HasConstraintName("fk_activity_project_profile_activities_activity_id");
        builder.HasIndex(p => p.ActivityId).IsUnique();

        builder.EnumColumn(p => p.DifficultyLevel);
        builder.EnumColumn(p => p.ReadinessStatus);

        builder.Property(p => p.ProjectArea).HasMaxLength(255).IsRequired();
        builder.Property(p => p.EstimatedHours).HasPrecision(6, 2).IsRequired();
        builder.Property(p => p.IsMessy).IsRequired();
        builder.Property(p => p.MaterialsNeeded).HasColumnType("jsonb");
        builder.Property(p => p.RequiredTools).HasColumnType("jsonb");
    }
}