using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Routines.application.dto.request.todoList;

public record RoutineTimePeriodRequest : TextColorRequest, ICreateRequest<RoutineTimePeriod>, IUpdateRequest<RoutineTimePeriod>
{
    public int LengthInDays { get; init; }
    public bool IsHidden { get; init; } = false;

    /// <summary>
    /// Weekly-aligned periods (LengthInDays &lt;= 7 or % 7 == 0): 0 = rolling, 1 = Mon … 7 = Sun.
    /// All other periods: 0 = rolling, 1–30 = day of month.
    /// </summary>
    public int ResetAnchorDay { get; init; }

    /// <summary>1–100 — minimum completion % to count the period as a streak success.</summary>
    public int StreakThreshold { get; init; } = 100;

    /// <summary>0 to LengthInDays-1 — extra days after the period ends before the streak breaks.</summary>
    public int StreakGraceDays { get; init; }

    /// <summary>1–100 — how many past periods to include in completion history.</summary>
    public int HistoryDepth { get; init; } = 16;

    /// <summary>
    /// 0 to <see cref="RoutineStreakFreezeService.MaxFreezeBudget"/> — streak freezes granted per calendar month.
    /// 0 means this period grants none. The remaining count is not settable: it is spent through the freeze
    /// endpoint and refilled by the service.
    /// </summary>
    public int FreezeBudget { get; init; } = RoutineStreakFreezeService.DefaultFreezeBudget;

    /// <summary>
    /// 1 to LengthInDays-1 — how many days before the reset to be nudged about unfinished items.
    /// <c>null</c> (the default) = no nudge for this period.
    /// </summary>
    public int? ReminderLeadDays { get; init; }


    public RoutineTimePeriod ToEntity => new()
    {
        Text = Text,
        Color = Color,
        LengthInDays = LengthInDays,
        IsHidden = IsHidden,
        ResetAnchorDay = ResetAnchorDay,
        StreakThreshold = StreakThreshold,
        StreakGraceDays = StreakGraceDays,
        HistoryDepth = HistoryDepth,
        ReminderLeadDays = ReminderLeadDays,
        FreezeBudget = FreezeBudget,
        // A new period starts its first window full, so the freeze is available before any read refreshes it.
        FreezesRemaining = FreezeBudget,
        UserId = 0
    };

    public void UpdateEntity(RoutineTimePeriod entity)
    {
        // Both inputs to RoutineResetService.ComputeNextReset. Read before the assignments below overwrite them:
        // if either moves, the reset instant moves with it and the nudge mark keyed to the old one is stale.
        // Clearing it unconditionally would instead re-nudge on every unrelated edit (a colour change mid-window).
        var scheduleChanged = entity.LengthInDays != LengthInDays || entity.ResetAnchorDay != ResetAnchorDay;

        entity.Text = Text;
        entity.Color = Color;
        entity.LengthInDays = LengthInDays;
        entity.IsHidden = IsHidden;
        entity.ResetAnchorDay = ResetAnchorDay;
        entity.StreakThreshold = StreakThreshold;
        entity.StreakGraceDays = StreakGraceDays;
        entity.HistoryDepth = HistoryDepth;
        entity.ReminderLeadDays = ReminderLeadDays;

        entity.FreezeBudget = FreezeBudget;
        // Lowering the budget mid-window must not leave more freezes banked than the new budget allows —
        // ck_routine_time_period_freezes_remaining_range would reject the update, and silently topping the
        // count up on the way down would hand out freezes for editing a setting.
        entity.FreezesRemaining = Math.Min(entity.FreezesRemaining, FreezeBudget);

        if (scheduleChanged)
            entity.EndingSoonNotifiedFor = null;

        // Streak, BestStreak, LastResetAt, StreakGraceUntil and GraceNotifiedFor are owned by
        // RoutineResetService and the nudge sweep — deliberately not writable from a request, so that editing a
        // period cannot hand anyone a streak or silence a warning.
    }
}