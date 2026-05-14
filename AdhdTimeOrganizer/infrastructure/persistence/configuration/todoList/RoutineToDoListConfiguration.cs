using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.infrastructure.persistence.converter;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

        builder.Property(r => r.SuggestedDays)
            .HasConversion(
                v => v.Select(d => (int)d).ToArray(),
                v => v.Select(i => (DayOfWeek)i).ToList(),
                new ValueComparer<List<DayOfWeek>>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    v => v.Aggregate(0, (h, d) => HashCode.Combine(h, d.GetHashCode())),
                    v => v.ToList()))
            .HasColumnType("integer[]");

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_RoutineTodoList_SuggestedDayOfMonth_Range",
            "\"suggested_day_of_month\" IS NULL OR (\"suggested_day_of_month\" BETWEEN 1 AND 31)"));

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