using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.reminder;

/// <summary>
/// A reminder the user set for themselves: "call the dentist at 15:00", or "nudge me before this planner
/// task". This entity owns <b>user intent</b> only — the title, when it should fire, and what (if anything)
/// it is attached to.
/// <para>
/// <b>Scheduler state lives in the Reminders module, not here.</b> Status, next occurrence and dispatch
/// history belong to <c>ReminderDefinition</c>, reached through the Kernel <c>IReminderRegistry</c> seam
/// (see <c>ReminderRegistrationService</c>). Do not mirror any of it onto this row — two sources of truth
/// for "did it fire yet" drift the first time a scan and a request interleave.
/// </para>
/// <para>
/// <b>The absence of a row is the "no reminder" state.</b> That is what makes reminders opt-in per planner
/// task rather than automatic for every task, and it is why this is a separate entity instead of a couple of
/// columns on <c>PlannerTask</c> — columns would cap the user at one reminder per task and would have
/// nowhere to put a standalone one.
/// </para>
/// </summary>
public class Reminder : BaseEntityWithUser
{
    /// <summary>The user's own text for their own nudge. Rendered verbatim as the notification body.</summary>
    public required string Title { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// The absolute instant the reminded-of thing happens (UTC). For a recurring reminder this doubles as the
    /// recurrence <i>anchor</i>, so a yearly birthday anchored years in the past is perfectly valid — the
    /// module's occurrence calculator walks forward from it.
    /// <para>
    /// Deliberately an instant and not a <c>CalendarId</c>: a recurring reminder belongs to no single day, and
    /// <c>Calendar</c> rows are only created for days the user has actually planned, so a date months out has
    /// no row to point at. The day view filters this column by the user's local-day range instead.
    /// </para>
    /// <para>
    /// For a reminder attached to a <see cref="PlannerTaskId"/> this column is <b>derived</b>, not authored:
    /// <c>ReminderRegistrationService</c> recomputes it from the task's <c>Calendar.Date</c> +
    /// <c>StartTime</c> (composed in the user's own time zone) on every sync, so the day view can query one
    /// column for both kinds of reminder without joining. The task stays authoritative.
    /// </para>
    /// </summary>
    public required DateTime RemindAt { get; set; }

    /// <summary>
    /// Occurrences relative to <see cref="RemindAt"/>, in minutes. Must be <c>&lt;= 0</c> and unique —
    /// <c>[-10, 0]</c> means "ten minutes before, and again at the time". The registry rejects a positive
    /// offset, so never write <c>[10]</c> meaning "ten minutes before".
    /// <para>
    /// A <b>recurring</b> reminder carries exactly one offset: the Kernel's recurring schedule has no
    /// lead-offset concept, so the single offset is folded into the recurrence anchor at registration.
    /// </para>
    /// </summary>
    public List<int> LeadOffsetsMinutes { get; set; } = [0];

    /// <summary><c>null</c> = fires once. Otherwise the cadence, anchored at <see cref="RemindAt"/>.</summary>
    public ReminderRecurrence? Recurrence { get; set; }

    /// <summary>
    /// The planner task this reminder is attached to, if any. A real FK rather than the module's string
    /// <c>SubjectType</c>/<c>SubjectId</c> pair: the portal is a small closed domain, and the cascade means no
    /// planner-task delete path can leave an orphaned reminder behind by forgetting to clean up.
    /// <para>Null = standalone. There is deliberately no second link column yet — see the class docs.</para>
    /// </summary>
    public long? PlannerTaskId { get; set; }

    public virtual PlannerTask? PlannerTask { get; set; }

    /// <summary>True when this reminder stands on its own rather than tracking a planner task's time.</summary>
    public bool IsStandalone => PlannerTaskId is null;
}