using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.infrastructure.persistence.converter;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.todoList;

public class RoutineTodoListConfiguration : IEntityTypeConfiguration<RoutineTodoList>
{
    public void Configure(EntityTypeBuilder<RoutineTodoList> builder)
    {
        builder.BaseEntityConfigure();

        builder.BaseTodoListConfigure();

        builder.Property(r => r.SuggestedTime)
            .HasConversion(new NullableIntTimeConverter());

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_RoutineTodoList_Streak_NonNegative",
            "\"streak\" >= 0"));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_RoutineTodoList_BestStreak_NonNegative",
            "\"best_streak\" >= 0"));

        builder.IsManyWithOneUser(u => u.RoutineTodoListColl);
        builder.IsManyWithOneActivity(a => a.RoutineTodoLists);

        builder.HasOne(r => r.RoutineTimePeriod)
            .WithMany(t => t.RoutineTodoListColl)
            .HasForeignKey(r => r.TimePeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        // UNIQUE INDEX
        builder.HasIndex(r => new { r.UserId, r.TimePeriodId, r.ActivityId })
            .IsUnique();

        // PERFORMANCE INDEX
        builder.HasIndex(r => new { r.UserId, r.TimePeriodId });
    }
}