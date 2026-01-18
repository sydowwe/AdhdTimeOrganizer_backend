using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.todoList;

public class RoutineTodoListConfiguration : IEntityTypeConfiguration<RoutineTodoList>
{
    public void Configure(EntityTypeBuilder<RoutineTodoList> builder)
    {
        builder.BaseEntityConfigure();

        builder.BaseTodoListConfigure();


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