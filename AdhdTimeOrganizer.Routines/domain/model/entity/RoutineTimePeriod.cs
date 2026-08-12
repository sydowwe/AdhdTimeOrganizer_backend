using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Routines.domain.service;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

public class RoutineTimePeriod : BaseEntityWithUser, IEntityWithIsHidden, IBaseTextColorEntity
{
    public required string Text { get; set; }
    public required string Color { get; set; }
    public bool IsHidden { get; set; }
    public required int LengthInDays { get; set; }
    public required int ResetAnchorDay { get; set; }

    public int Streak { get; set; } = 0;
    public int BestStreak { get; set; } = 0;
    public required int StreakThreshold { get; set; }
    public required int StreakGraceDays { get; set; }
    public DateTime? LastResetAt { get; set; }
    public DateTime? StreakGraceUntil { get; set; }

    public int HistoryDepth { get; set; } = 16;

    /// <summary>
    /// How many streak freezes this period grants per refill window. A freeze is spent on an elapsed period
    /// that fell short of <see cref="StreakThreshold"/> and makes it carry the streak instead of breaking it.
    /// <para>0 disables freezes for this period without hiding the feature — the client still renders the
    /// budget, it just reads "0 left". Capped at <see cref="RoutineStreakFreezeService.MaxFreezeBudget"/> by a
    /// CHECK constraint, because a budget as large as the history depth would make the streak meaningless.</para>
    /// </summary>
    public int FreezeBudget { get; set; } = RoutineStreakFreezeService.DefaultFreezeBudget;

    /// <summary>
    /// Freezes still spendable in the current window. Refilled to <see cref="FreezeBudget"/> lazily by
    /// <see cref="RoutineStreakFreezeService.RefreshFreezeBudget"/> once <see cref="FreezeBudgetResetsAt"/>
    /// passes — there is no job for it, because every surface that shows or spends the budget refreshes first.
    /// </summary>
    public int FreezesRemaining { get; set; } = RoutineStreakFreezeService.DefaultFreezeBudget;

    /// <summary>
    /// When the freeze budget next refills — midnight UTC on the first of the next calendar month. The window
    /// is deliberately a calendar month rather than one period length: "two freezes per period" would hand a
    /// daily routine two skips a day, which is not leniency, it is switching the streak off.
    /// <para><c>null</c> until the first refresh opens a window; treated as "due now".</para>
    /// </summary>
    public DateTime? FreezeBudgetResetsAt { get; set; }

    /// <summary>
    /// How many days before the period resets to nudge about unfinished items. <c>null</c> = no lead-time
    /// nudge for this period, which is the deliberate default: the reminder is opt-in per period rather than
    /// per user, because "remind me about my weekly chores" and "remind me about my yearly review" are not the
    /// same decision. Must be less than <see cref="LengthInDays"/> — a one-day period has no valid lead.
    /// <para>Gates <c>RoutinePeriodEndingSoon</c> only. The end-of-period summary and the grace warning fire on
    /// their own conditions; the user mutes those through the notification preference matrix.</para>
    /// </summary>
    public int? ReminderLeadDays { get; set; }

    /// <summary>
    /// The reset instant a lead-time nudge has already been sent for — the sweep's idempotency mark, not user
    /// data. Compared against <c>RoutineResetService.ComputeNextReset</c>, so it survives the reset instant
    /// moving (an anchor or length edit re-derives a different instant and correctly earns a fresh nudge).
    /// <para>Written only when a notification actually goes out. A period that is already fully done when the
    /// window opens is left unmarked on purpose, so un-ticking an item the next day still earns its nudge.</para>
    /// </summary>
    public DateTime? EndingSoonNotifiedFor { get; set; }

    /// <summary>
    /// The <see cref="StreakGraceUntil"/> value a grace warning has already been sent for. Same idempotency
    /// role as <see cref="EndingSoonNotifiedFor"/>; keyed on the grace instant so a new grace window (a later
    /// failed period) is a new warning rather than a suppressed duplicate.
    /// </summary>
    public DateTime? GraceNotifiedFor { get; set; }

    public ICollection<RoutineTodoList> RoutineTodoListColl { get; set; } = [];
    public ICollection<RoutinePeriodCompletion> CompletionHistoryColl { get; set; } = [];
}