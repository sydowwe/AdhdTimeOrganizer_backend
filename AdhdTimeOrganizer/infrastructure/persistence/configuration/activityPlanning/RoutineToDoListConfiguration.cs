using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class RoutineTodoListConfiguration : IEntityTypeConfiguration<RoutineTodoList>
{
    public void Configure(EntityTypeBuilder<RoutineTodoList> builder)
    {
        builder.BaseEntityConfigure();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_RoutineTodoList_DoneCount_Min", "done_count >= 0");
            t.HasCheckConstraint("CK_RoutineTodoList_TotalCount_Range", "total_count >= 2 AND total_count <= 99");
            t.HasCheckConstraint("CK_RoutineTodoList_DoneCount_LessOrEqual_TotalCount", "done_count <= total_count");
        });

        builder.Property(p => p.IsDone).HasDefaultValue(false).IsRequired();
        builder.IsManyWithOneUser(u => u.RoutineTodoListColl);
        builder.IsManyWithOneActivity(a=>a.RoutineTodoLists);

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