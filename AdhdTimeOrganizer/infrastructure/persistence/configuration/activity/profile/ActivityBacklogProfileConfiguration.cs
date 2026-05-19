using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity.profile;

public class ActivityBacklogProfileConfiguration : IEntityTypeConfiguration<ActivityBacklogProfile>
{
    public void Configure(EntityTypeBuilder<ActivityBacklogProfile> builder)
    {
        builder.BaseEntityConfigure();

        builder.HasOne(p => p.Activity)
            .WithOne(a => a.BacklogProfile)
            .HasForeignKey<ActivityBacklogProfile>(p => p.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);
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
        builder.EnumColumn(p => p.EffortType, isRequired: false);

        builder.Property(p => p.MinParticipants).IsRequired();
        builder.Property(p => p.DurationMinutes).IsRequired();
        builder.Property(p => p.IsRepeatable).IsRequired();
    }
}
