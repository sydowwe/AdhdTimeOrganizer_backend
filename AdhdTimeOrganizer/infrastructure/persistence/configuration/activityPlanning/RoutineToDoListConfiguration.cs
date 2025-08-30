using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class RoutineTodoListConfiguration : IEntityTypeConfiguration<RoutineTodoList>
{
    public void Configure(EntityTypeBuilder<RoutineTodoList> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(p => p.IsDone).HasDefaultValue(false).IsRequired();
        builder.IsManyWithOneUser(u => u.RoutineTodoListColl);
        builder.IsOneWithOneActivity(a=>a.RoutineTodoList);

        builder.HasOne(r => r.RoutineTimePeriod)
            .WithMany(t=>t.RoutineTodoListColl)
            .HasForeignKey(r => r.TimePeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        // UNIQUE INDEX
        builder.HasIndex(r => new { r.UserId, r.TimePeriodId, r.ActivityId })
            .IsUnique();

        // PERFORMANCE INDEX
        builder.HasIndex(r => new { r.UserId, r.TimePeriodId });
    }
}