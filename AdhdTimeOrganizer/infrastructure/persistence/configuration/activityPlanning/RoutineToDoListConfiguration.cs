using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class RoutineToDoListConfiguration : IEntityTypeConfiguration<RoutineToDoList>
{
    public void Configure(EntityTypeBuilder<RoutineToDoList> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(p => p.IsDone).HasDefaultValue(false).IsRequired();
        builder.IsManyWithOneUser(u => u.RoutineToDoListColl);
        builder.IsOneWithOneActivity(a=>a.RoutineToDoList);

        builder.HasOne(r => r.TimePeriod)
            .WithMany(t=>t.RoutineToDoListColl)
            .HasForeignKey(r => r.TimePeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        // UNIQUE INDEX
        builder.HasIndex(r => new { r.UserId, r.TimePeriodId, r.ActivityId })
            .IsUnique();

        // PERFORMANCE INDEX
        builder.HasIndex(r => new { r.UserId, r.TimePeriodId });
    }
}