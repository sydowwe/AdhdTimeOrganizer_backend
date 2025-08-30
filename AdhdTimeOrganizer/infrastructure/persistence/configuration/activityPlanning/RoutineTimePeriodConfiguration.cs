using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class RoutineTimePeriodConfiguration : IEntityTypeConfiguration<RoutineTimePeriod>
{
    public void Configure(EntityTypeBuilder<RoutineTimePeriod> builder)
    {
        builder.BaseTextColorEntityConfigure();
        builder.Property(t=>t.LengthInDays).IsRequired();

        builder.HasMany(r => r.RoutineToDoListColl)
            .WithOne(t => t.TimePeriod)
            .HasForeignKey(t => t.TimePeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.LengthInDays }).IsUnique();

    }
}