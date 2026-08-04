using MojaDigitalnaFirma.Kernel.reminders;

namespace MojaDigitalnaFirma.Kernel.notification.payload;

// One record per NotificationType — the compile-time shape of what each producer may persist.
// Read INotificationPayload first: ids and non-person scalars only, never free-text person data.
// Property names are the persisted camelCase JSON keys (JsonHelper's naming policy) and the renderer
// deserializes these exact records, so renaming one is a storage change: old rows keep the old key and
// deserialize to null, which must still hit a name-less fallback branch in NotificationTextRenderer.
//
// A record here holds ids and non-person scalars only. Anything person-identifying is an overlay written
// onto the JSON at render time by an INotificationPayloadEnricher, never persisted payload.

/// <summary>
/// A dated obligation is coming due. <c>Title</c> is composed by the producing module and must itself be
/// person-data-free — Zmluvy builds it from the register number + the date, never a contract subject.
/// </summary>
[NotificationPayload(NotificationType.DeadlineApproaching)]
public sealed record DeadlineApproachingPayload(string? Title = null) : INotificationPayload;

/// <summary>Dev/QA pipeline check. <c>Message</c> is operator-authored text — keep it PII-free.</summary>
[NotificationPayload(NotificationType.Test)]
public sealed record TestNotificationPayload(string? Message = null) : INotificationPayload;

/// <summary>Aggregated reminder digest: a total plus a count-per-<c>Kind</c> breakdown (non-person category strings).</summary>
[NotificationPayload(NotificationType.ReminderDigest)]
public sealed record ReminderDigestPayload(int Count, IReadOnlyList<ReminderDigestKindCount>? Kinds = null) : INotificationPayload;

/// <param name="Kind">The <c>ReminderDefinition.Kind</c> category string.</param>
public sealed record ReminderDigestKindCount(string Kind, int Count);

/// <summary>
/// A reminder the user set for themselves. <c>Title</c> is the user's own text for their own nudge — free
/// text, but it is <b>their</b> text about <b>their</b> reminder, and the portal row it is copied from is
/// deleted with their account. Keep it that way: nothing person-shaped about anyone else belongs here, and
/// nothing here may name a third party. <c>PlannerTaskId</c> is present so the client can deep-link back to
/// the task; a standalone reminder leaves it null.
/// <para>
/// Implements <see cref="IReminderPayload"/> as well because it is written at <i>registration</i> time
/// (persisted to <c>ReminderDefinition.PayloadJson</c>) and read back at <i>dispatch</i> time by the
/// renderer — both markers apply to the same document, and both are walked by the same PII guard.
/// </para>
/// </summary>
[NotificationPayload(NotificationType.PersonalReminder)]
public sealed record PersonalReminderPayload(
    long ReminderId,
    string? Title = null,
    long? PlannerTaskId = null) : INotificationPayload, IReminderPayload;

/// <summary>
/// A routine period is about to reset with work outstanding. <c>PeriodName</c> is the user's own label for
/// their own routine period ("Weekly", "Chores") — free text, but it is <b>their</b> text about <b>their</b>
/// row, and the portal row it is copied from is deleted with their account. Same rule as
/// <see cref="PersonalReminderPayload"/>: nothing person-shaped about anyone else belongs here.
/// <para>
/// The counts are a snapshot taken by the sweep at nudge time, not at any earlier registration — that is the
/// whole reason this type is swept for rather than scheduled through the Reminders module.
/// </para>
/// </summary>
[NotificationPayload(NotificationType.RoutinePeriodEndingSoon)]
public sealed record RoutinePeriodEndingSoonPayload(
    long PeriodId,
    string? PeriodName = null,
    int Remaining = 0,
    int Total = 0,
    int DaysLeft = 0,
    int Streak = 0) : INotificationPayload;

/// <summary>
/// A routine period reset. Carries the completion snapshot the reset wrote to <c>RoutinePeriodCompletion</c>
/// plus what it did to the streak, so the body can say "kept" / "broken" without a second read.
/// <c>Streak</c> is the value <i>after</i> the reset. See <see cref="RoutinePeriodEndingSoonPayload"/> for the
/// rule on <c>PeriodName</c>.
/// </summary>
[NotificationPayload(NotificationType.RoutinePeriodEnded)]
public sealed record RoutinePeriodEndedPayload(
    long PeriodId,
    string? PeriodName = null,
    int CompletedCount = 0,
    int TotalCount = 0,
    int Streak = 0,
    RoutineStreakOutcome Outcome = RoutineStreakOutcome.Unknown) : INotificationPayload;

/// <summary>What a period reset did to the streak. <see cref="Unknown"/> is the shape-drift fallback.</summary>
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
/// A period's streak grace window is about to lapse. See <see cref="RoutinePeriodEndingSoonPayload"/> for the
/// rule on <c>PeriodName</c>. <c>Streak</c> is the streak that is at stake.
/// </summary>
[NotificationPayload(NotificationType.RoutineStreakGraceExpiring)]
public sealed record RoutineStreakGraceExpiringPayload(
    long PeriodId,
    string? PeriodName = null,
    DateTime? GraceUntil = null,
    int Streak = 0) : INotificationPayload;

/// <summary>A recurring background job failed terminally. Technical identifiers only.</summary>
[NotificationPayload(NotificationType.ScheduledJobFailed)]
public sealed record ScheduledJobFailedPayload(
    string? JobKey = null,
    string? OwnerModule = null,
    string? ErrorType = null,
    long? RunId = null) : INotificationPayload;

/// <summary>
/// A recurring background job never fired (scheduler follow-up 08). Technical identifiers only — and no
/// <c>RunId</c>, because the absence of a run row is the condition being reported. <c>ExpectedRunAt</c> is the
/// fire it missed, so the renderer can say how long it has been silent.
/// </summary>
[NotificationPayload(NotificationType.ScheduledJobOverdue)]
public sealed record ScheduledJobOverduePayload(
    string? JobKey = null,
    string? OwnerModule = null,
    DateTime? ExpectedRunAt = null) : INotificationPayload;