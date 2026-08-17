using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Routines.infrastructure.persistence.configuration.todoList;

public class RoutineTimePeriodConfiguration : IEntityTypeConfiguration<RoutineTimePeriod>
{
    public void Configure(EntityTypeBuilder<RoutineTimePeriod> builder)
    {
        builder.BaseTextColorEntityConfigure();
        builder.HasIndex(t => new { t.UserId, t.Text }).IsUnique();

        builder.Property(t => t.HistoryDepth).IsRequired();
        builder.Property(t => t.ResetAnchorDay).IsRequired();
        builder.Property(t => t.LengthInDays).IsRequired();
        builder.Property(t => t.Streak).IsRequired();
        builder.Property(t => t.BestStreak).IsRequired();
        builder.Property(t => t.StreakThreshold).IsRequired();
        builder.Property(t => t.StreakGraceDays).IsRequired();
        builder.Property(t => t.FreezeBudget).IsRequired();
        builder.Property(t => t.FreezesRemaining).IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_reset_anchor_day_range",
            "((\"length_in_days\" <= 7 OR \"length_in_days\" % 7 = 0) AND \"reset_anchor_day\" BETWEEN 0 AND 7) OR " +
            "((\"length_in_days\" > 7 AND \"length_in_days\" % 7 <> 0) AND \"reset_anchor_day\" BETWEEN 0 AND 30)"));

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

        // NULL = no lead-time nudge, which is the default. A lead that reaches back past the start of the
        // period would make the nudge permanent, so it is capped below the period length — which also means a
        // one-day period admits no lead at all and can only ever be NULL.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_reminder_lead_days_range",
            "\"reminder_lead_days\" IS NULL OR (\"reminder_lead_days\" >= 1 AND \"reminder_lead_days\" < \"length_in_days\")"));

        // A budget the size of the history depth would mean every miss can be papered over, so the streak would
        // stop meaning anything. 0 is legal and means "this period grants no freezes" — the client still shows
        // the chip, reading zero, rather than hiding the feature (which is what a null budget signals).
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_freeze_budget_range",
            $"\"freeze_budget\" >= 0 AND \"freeze_budget\" <= {RoutineStreakFreezeService.MaxFreezeBudget}"));

        // Held by RoutineStreakFreezeService: the refill sets it to the budget, spending decrements it, and an
        // update that lowers the budget clamps it. Nothing else writes it.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_time_period_freezes_remaining_range",
            "\"freezes_remaining\" >= 0 AND \"freezes_remaining\" <= \"freeze_budget\""));

        builder.HasMany(r => r.RoutineTodoListColl)
            .WithOne(t => t.RoutineTimePeriod)
            .HasForeignKey(t => t.TimePeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.LengthInDays }).IsUnique();
    }
}