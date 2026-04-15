using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.todoList;

public class RoutineTimePeriodConfiguration : IEntityTypeConfiguration<RoutineTimePeriod>
{
    public void Configure(EntityTypeBuilder<RoutineTimePeriod> builder)
    {
        builder.BaseTextColorEntityConfigure();

        builder.Property(t => t.HistoryDepth).IsRequired();
        builder.Property(t => t.ResetAnchorDay).IsRequired();
        builder.Property(t => t.LengthInDays).IsRequired();
        builder.Property(t => t.Streak).IsRequired();
        builder.Property(t => t.BestStreak).IsRequired();
        builder.Property(t => t.StreakThreshold).IsRequired();
        builder.Property(t => t.StreakGraceDays).IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_reset_anchor_day_range",
            "(\"length_in_days\" <= 7 OR \"length_in_days\" % 7 = 0 AND \"reset_anchor_day\" BETWEEN 1 AND 7) OR " +
            "(\"length_in_days\" > 7 AND \"length_in_days\" % 7 <> 0 AND \"reset_anchor_day\" BETWEEN 1 AND 30)"));

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_length_in_days_range",
            "\"length_in_days\" >= 1 AND \"length_in_days\" <= 365"));

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_streak_non_negative",
            "\"streak\" >= 0"));

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_best_streak_non_negative",
            "\"best_streak\" >= 0"));

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_streak_threshold_range",
            "\"streak_threshold\" >= 1 AND \"streak_threshold\" <= 100"));

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_streak_grace_days_range",
            "\"streak_grace_days\" >= 0 AND \"streak_grace_days\" <= \"length_in_days\" - 1"));

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_history_depth_range",
            "\"history_depth\" >= 1 AND \"history_depth\" <= 100"));

        builder.HasMany(r => r.RoutineTodoListColl)
            .WithOne(t => t.RoutineTimePeriod)
            .HasForeignKey(t => t.TimePeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.LengthInDays }).IsUnique();
    }
}