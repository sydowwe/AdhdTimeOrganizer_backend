using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.domain.service;

public static class RoutineResetService
{
    // Weekly-aligned: period is <= 7 days OR a multiple of 7 → anchor = day of week (1=Mon…7=Sun)
    // Everything else (10d, 20d, 45d…)                       → anchor = day of month (1–30)
    private static bool IsWeeklyAligned(RoutineTimePeriod period) =>
        period.LengthInDays <= 7 || period.LengthInDays % 7 == 0;

    public static DateTime ComputeNextReset(RoutineTimePeriod period)
    {
        var lastReset = period.LastResetAt ?? period.CreatedTimestamp;
        var earliest = DateTime.SpecifyKind(lastReset.AddDays(period.LengthInDays).Date, DateTimeKind.Utc);

        if (period.ResetAnchorDay == 0)
            return earliest;

        if (IsWeeklyAligned(period))
        {
            var targetDow = period.ResetAnchorDay == 7
                ? DayOfWeek.Sunday
                : (DayOfWeek)period.ResetAnchorDay;

            var daysUntil = ((int)targetDow - (int)earliest.DayOfWeek + 7) % 7;
            return earliest.AddDays(daysUntil);
        }
        else
        {
            var targetDay = period.ResetAnchorDay; // 1–28
            int year, month;

            if (period.LengthInDays == 30)
            {
                // Calendar-month aligned: next reset is targetDay of the next calendar month
                month = lastReset.Month + 1;
                year = lastReset.Year;
                if (month > 12) { month = 1; year++; }
            }
            else if (period.LengthInDays == 365)
            {
                // Calendar-year aligned: next reset is targetDay of the same month next year
                month = lastReset.Month;
                year = lastReset.Year + 1;
            }
            else
            {
                // Day-of-month anchor: find next occurrence of that day on or after earliest
                year = earliest.Year;
                month = earliest.Month;

                if (earliest.Day > targetDay)
                {
                    month++;
                    if (month > 12) { month = 1; year++; }
                }
            }

            var day = Math.Min(targetDay, DateTime.DaysInMonth(year, month));
            return new DateTime(year, month, day, 2, 0, 0, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Breaks the streak if the grace period has expired. Should be called before TryReset on every access.
    /// Returns true if the period was modified.
    /// </summary>
    public static bool CheckGrace(RoutineTimePeriod period, DateTime now)
    {
        if (period.StreakGraceUntil == null || now <= period.StreakGraceUntil.Value)
            return false;

        period.Streak = 0;
        period.StreakGraceUntil = null;
        return true;
    }

    /// <summary>
    /// Resets a single item if the period has elapsed. Does not evaluate period streak.
    /// Use when only one item is in context (e.g. step toggle). Returns true if a reset occurred.
    /// </summary>
    public static bool TryReset(RoutineTimePeriod period, RoutineTodoList item, DateTime now)
    {
        var nextReset = ComputeNextReset(period);
        if (now < nextReset)
            return false;

        var today = DateOnly.FromDateTime(now);
        item.IsDone = false;
        item.DoneCount = 0;
        item.LastResetDate = today;
        foreach (var step in item.Steps)
            step.IsDone = false;

        period.LastResetAt = nextReset;
        return true;
    }

    /// <summary>
    /// Resets items if the period has elapsed. Evaluates period streak before clearing.
    /// Returns a <see cref="RoutinePeriodCompletion"/> record if a reset occurred, otherwise null.
    /// </summary>
    public static RoutinePeriodCompletion? TryReset(RoutineTimePeriod period, IList<RoutineTodoList> items, DateTime now)
    {
        var nextReset = ComputeNextReset(period);
        if (now < nextReset)
            return null;

        var completedCount = 0;
        if (items.Count > 0)
        {
            completedCount = items.Count(i => i.IsDone);
            var completionPercent = (double)completedCount / items.Count * 100.0;

            if (completionPercent >= period.StreakThreshold)
            {
                period.Streak++;
                if (period.Streak > period.BestStreak)
                    period.BestStreak = period.Streak;
                period.StreakGraceUntil = null;
            }
            else if (period.StreakGraceDays > 0)
            {
                period.StreakGraceUntil = nextReset.AddDays(period.StreakGraceDays);
            }
            else
            {
                period.Streak = 0;
                period.StreakGraceUntil = null;
            }
        }

        var today = DateOnly.FromDateTime(now);
        foreach (var item in items)
        {
            item.IsDone = false;
            item.DoneCount = 0;
            item.LastResetDate = today;
            foreach (var step in item.Steps)
                step.IsDone = false;
        }

        var periodEnd = DateOnly.FromDateTime(nextReset);
        period.LastResetAt = nextReset;

        return new RoutinePeriodCompletion
        {
            TimePeriodId = period.Id,
            PeriodStart = periodEnd.AddDays(-period.LengthInDays),
            PeriodEnd = periodEnd,
            CompletedCount = completedCount,
            TotalCount = items.Count,
            CreatedAt = now
        };
    }

    /// <summary>
    /// Updates the item's streak after a toggle. Call after IsDoneLogic has been applied.
    /// </summary>
    public static void UpdateItemStreak(RoutineTodoList item, DateTime now)
    {
        if (!item.IsDone)
            return;

        item.LastCompletedAt = now;
        item.Streak++;
        if (item.Streak > item.BestStreak)
            item.BestStreak = item.Streak;
    }
}
