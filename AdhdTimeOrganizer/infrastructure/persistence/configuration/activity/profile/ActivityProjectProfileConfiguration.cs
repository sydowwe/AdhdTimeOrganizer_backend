using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity.profile;

public class ActivityProjectProfileConfiguration : IEntityTypeConfiguration<ActivityProjectProfile>
{
    public void Configure(EntityTypeBuilder<ActivityProjectProfile> builder)
    {
        builder.BaseEntityConfigure();

        builder.HasOne(p => p.Activity)
            .WithOne(a => a.ProjectProfile)
            .HasForeignKey<ActivityProjectProfile>(p => p.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);
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
