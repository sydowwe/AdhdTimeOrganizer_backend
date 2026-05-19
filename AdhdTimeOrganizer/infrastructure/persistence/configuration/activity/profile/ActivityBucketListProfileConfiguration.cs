using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity.profile;

public class ActivityBucketListProfileConfiguration : IEntityTypeConfiguration<ActivityBucketListProfile>
{
    public void Configure(EntityTypeBuilder<ActivityBucketListProfile> builder)
    {
        builder.BaseEntityConfigure();

        builder.HasOne(p => p.Activity)
            .WithOne(a => a.BucketListProfile)
            .HasForeignKey<ActivityBucketListProfile>(p => p.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);
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
