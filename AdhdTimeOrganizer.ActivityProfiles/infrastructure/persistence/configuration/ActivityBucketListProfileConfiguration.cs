using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.configuration;

public class ActivityBucketListProfileConfiguration : IEntityTypeConfiguration<ActivityBucketListProfile>
{
    public void Configure(EntityTypeBuilder<ActivityBucketListProfile> builder)
    {
        builder.BaseEntityConfigure();

        builder.HasOne(p => p.Activity)
            .WithOne()
            .HasForeignKey<ActivityBucketListProfile>(p => p.ActivityId)
            .OnDelete(DeleteBehavior.Cascade)
            // Pinned — see ActivityBacklogProfileConfiguration.
            .HasConstraintName("fk_activity_bucket_list_profile_activities_activity_id");
        builder.HasIndex(p => p.ActivityId).IsUnique();

        builder.HasOne(p => p.ExperienceType)
            .WithMany()
            .HasForeignKey(p => p.ExperienceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.ComfortZoneStep).IsRequired();
        builder.Property(p => p.RequiresTravel).IsRequired();
        builder.Property(p => p.InspirationSource).HasMaxLength(500).IsRequired();
        builder.Property(p => p.FinancialGoal).HasPrecision(12, 2);
    }
}