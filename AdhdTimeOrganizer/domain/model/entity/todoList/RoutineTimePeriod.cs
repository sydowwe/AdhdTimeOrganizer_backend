using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

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