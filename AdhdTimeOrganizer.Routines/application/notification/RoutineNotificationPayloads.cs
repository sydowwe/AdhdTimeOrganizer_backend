using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.Routines.application.notification;

// Read INotificationPayload first: ids and non-person scalars only. Property names are the persisted camelCase JSON
// keys, so renaming one is a storage change — an old row keeps the old key and its kind renders the fallback.

/// <summary>
/// A routine period is about to reset with work outstanding. <c>PeriodName</c> is the user's own label for their own
/// routine period ("Weekly", "Chores") — free text, but it is <b>their</b> text about <b>their</b> row, and the row it
/// is copied from is deleted with their account. Nothing person-shaped about anyone else belongs here.
/// <para>
/// The counts are a snapshot taken by the sweep at nudge time, not at any earlier registration — that is the whole
/// reason this kind is swept for rather than scheduled through the Reminders module.
/// </para>
/// </summary>
[NotificationPayload(RoutinesNotificationTypes.RoutinePeriodEndingSoonKey)]
public sealed record RoutinePeriodEndingSoonPayload(
    long PeriodId,
    string? PeriodName = null,
    int Remaining = 0,
    int Total = 0,
    int DaysLeft = 0,
    int Streak = 0) : INotificationPayload;

/// <summary>
/// A routine period reset. Carries the completion snapshot the reset wrote to <c>RoutinePeriodCompletion</c> plus what
/// it did to the streak, so the body can say "kept" / "broken" without a second read. <c>Streak</c> is the value
/// <i>after</i> the reset. See <see cref="RoutinePeriodEndingSoonPayload"/> for the rule on <c>PeriodName</c>.
/// <para>
/// The last three are for a <see cref="RoutineStreakOutcome.Broken"/> reset, where <c>Streak</c> is already 0 and so
/// says nothing: <c>LostStreak</c> is the run the break ended, <c>FreezesRemaining</c> the period's budget once
/// refilled, and <c>CanFreeze</c> whether spending one on this miss <i>right now</i> would bring the run back — the
/// body only offers the freeze when it is true. Appended with defaults, so a row persisted before they existed
/// deserializes to "no freeze offered" and renders the plain broken sentence.
/// </para>
/// <para>
/// The next four are for an <see cref="RoutineStreakOutcome.Extended"/> reset worth more than "streak continues".
/// <c>IsNewBest</c> means this cycle took the run past the record a previous run set, once per run;
/// <c>PreviousBest</c> is that record. <c>IsMilestone</c> means <c>Streak</c> just landed on one of the owner's
/// milestones. Both are decided by the producer, which owns those rules. <c>PeriodLengthInDays</c> lets the wording
/// count the run in its own unit ("7 weeks in a row", not "7 periods"). All four are appended with defaults, so an
/// older row renders the plain extended sentence.
/// </para>
/// </summary>
[NotificationPayload(RoutinesNotificationTypes.RoutinePeriodEndedKey)]
public sealed record RoutinePeriodEndedPayload(
    long PeriodId,
    string? PeriodName = null,
    int CompletedCount = 0,
    int TotalCount = 0,
    int Streak = 0,
    RoutineStreakOutcome Outcome = RoutineStreakOutcome.Unknown,
    int LostStreak = 0,
    int FreezesRemaining = 0,
    bool CanFreeze = false,
    bool IsNewBest = false,
    int PreviousBest = 0,
    bool IsMilestone = false,
    int PeriodLengthInDays = 0) : INotificationPayload;

/// <summary>
/// What a period reset did to the streak, as the persisted payload states it. <see cref="Unknown"/> is the shape-drift
/// fallback. Deliberately apart from the domain's <c>StreakOutcome</c>, which has members (an excused cycle) this
/// stored contract never carries. Member names are persisted: only append.
/// </summary>
public enum RoutineStreakOutcome
{
    Unknown = 0,

    /// <summary>Completion met the period's threshold — streak incremented.</summary>
    Extended,

    /// <summary>Threshold missed, but the period allows grace days, so the streak is held pending expiry.</summary>
    OnGrace,

    /// <summary>Threshold missed with no grace left — streak reset to zero.</summary>
    Broken
}

/// <summary>
/// A period's streak grace window is about to lapse. See <see cref="RoutinePeriodEndingSoonPayload"/> for the rule on
/// <c>PeriodName</c>. <c>Streak</c> is the streak that is at stake.
/// </summary>
[NotificationPayload(RoutinesNotificationTypes.RoutineStreakGraceExpiringKey)]
public sealed record RoutineStreakGraceExpiringPayload(
    long PeriodId,
    string? PeriodName = null,
    DateTime? GraceUntil = null,
    int Streak = 0) : INotificationPayload;

/// <summary>
/// The routine items suggested for the user's today and not done yet, in one digest. <c>Date</c> is the user's
/// <b>local</b> date the digest is about, so a delivery deferred past quiet hours still names its day honestly. Each
/// item's <c>Name</c> is the user's own label for their own activity. <c>PeriodId</c> is there only so the subject can
/// be derived: every item in one period points at that period, items spread over several point at nothing.
/// <para>
/// <c>Count</c> is the number of qualifying items. It equals <c>Items.Count</c> as written today; it is carried
/// separately so the body can say "and 2 more" without the wording depending on the list being complete.
/// </para>
/// </summary>
[NotificationPayload(RoutinesNotificationTypes.RoutineSuggestedTodayKey)]
public sealed record RoutineSuggestedTodayPayload(
    DateOnly? Date = null,
    IReadOnlyList<RoutineSuggestedTodayItem>? Items = null,
    int Count = 0) : INotificationPayload;

/// <param name="ItemId">The routine to-do item's id.</param>
/// <param name="Name">The item's activity name — the user's own label for their own row.</param>
/// <param name="PeriodId">The routine period the item belongs to.</param>
public sealed record RoutineSuggestedTodayItem(long ItemId, string? Name = null, long PeriodId = 0);
