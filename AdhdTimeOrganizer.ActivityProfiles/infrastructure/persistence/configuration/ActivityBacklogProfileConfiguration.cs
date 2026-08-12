using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.configuration;

public class ActivityBacklogProfileConfiguration : IEntityTypeConfiguration<ActivityBacklogProfile>
{
    public void Configure(EntityTypeBuilder<ActivityBacklogProfile> builder)
    {
        builder.BaseEntityConfigure();

        builder.HasOne(p => p.Activity)
            .WithOne()
            .HasForeignKey<ActivityBacklogProfile>(p => p.ActivityId)
            .OnDelete(DeleteBehavior.Cascade)
            // Pinned. EF derives this name from the principal-end navigation, so deleting
            // Activity.BacklogProfile in the slice extraction would silently rename it to
            // fk_activity_backlog_profile_activity_activity_id — a DROP + ADD CONSTRAINT pair that
            // takes an ACCESS EXCLUSIVE lock on activity_backlog_profile and revalidates every row,
            // for no schema benefit. Same reasoning as PlannerTaskConfiguration's pinned name.
            .HasConstraintName("fk_activity_backlog_profile_activities_activity_id");
        builder.HasIndex(p => p.ActivityId).IsUnique();

        builder.HasOne(p => p.LocationType)
            .WithMany()
            .HasForeignKey(p => p.LocationTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.WeatherDependency)
            .WithMany()
            .HasForeignKey(p => p.WeatherDependencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ExpectedCostTier)
            .WithMany()
            .HasForeignKey(p => p.ExpectedCostTierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.EnumColumn(p => p.EnergyLevel);
        builder.EnumColumn(p => p.EffortType, false);

        builder.Property(p => p.MinParticipants).IsRequired();
        builder.Property(p => p.DurationMinutes).IsRequired();
        builder.Property(p => p.IsRepeatable).IsRequired();
    }
}