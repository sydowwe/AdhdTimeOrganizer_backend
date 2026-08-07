using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Notifications.domain.entity;

/// <summary>
/// A user's quiet-hours window — at most one row per user, and the <b>single</b> window in the deployment:
/// the Reminders module used to own a parallel <c>ReminderQuietHours</c> table and now reads this one through
/// <see cref="IQuietHoursReader"/> (notifications follow-up 05).
/// <para>
/// <b>Effect (Notifications side).</b> During the window the <b>background</b> channels are <i>deferred</i>,
/// never dropped: <c>NotificationService</c> stamps <see cref="Notification.DeferredUntil"/> with
/// <see cref="QuietHoursPolicy.ResumeAt"/> and withholds Web Push + Email; the history row and the InApp
/// (SignalR) push still go out immediately, because a connected user is not being disturbed by content
/// appearing in a bell they are already looking at. <c>Notifications.FlushDeferredNotifications</c> delivers
/// the withheld channels once the window has passed. The bell / <c>mine</c> list ignores this column.
/// </para>
/// <para>
/// <b>Effect (Reminders side).</b> The scan defers a due occurrence when every recipient is inside their
/// window; it holds <c>NextOccurrenceAt</c> and re-evaluates on the next scan, so no deferred-delivery state
/// is needed there (reminders are pull-scanned).
/// </para>
/// <para>
/// <b>Window encoding:</b> <see cref="StartMinute"/> / <see cref="EndMinute"/> are minutes-from-midnight
/// (<c>0..1439</c>) in the <b>deployment-configured time zone</b> (<c>Application:Timezone</c>). The repo
/// models <b>no per-user/employee time zone</b> — only this single deployment-wide zone (the one
/// <c>WorkHourCalculatorService</c> uses). If a per-user time zone is ever added, revisit.
/// <c>Start &gt; End</c> means an overnight window (e.g. 22:00→06:00); <c>Start == End</c> is rejected at the
/// endpoint (clear the window instead of encoding an ambiguous zero/all-day span).
/// </para>
/// <para>
/// <b>Opt-in:</b> no row means no quiet hours, and none is ever seeded. <c>[NoAudit]</c> mirrors
/// <see cref="NotificationPreference"/> — user settings, not history.
/// </para>
/// <para>
/// <b>Flagged: <see cref="UserId"/> is a plain unique-indexed column with no FK to the user table</b> — unlike
/// this module's <see cref="Notification"/> / <see cref="NotificationPreference"/> / <see cref="PushSubscription"/>,
/// and deliberately so. The row is a settings lookup that the Reminders scan probes with whatever recipient
/// ids an <c>IReminderRecipientResolver</c> produced (reminder recipients carry no FK either), not a
/// user-owned aggregate; keeping the shape it had as <c>ReminderQuietHours</c> also made the move a pure
/// data copy. The cost is no cascade on user deletion — a stale window (three ints) can outlive its user.
/// If that becomes a concern, add the FK together with a backfill that prunes orphans first.
/// </para>
/// </summary>
[NoAudit]
public class NotificationQuietHours : BaseTableEntity
{
    public long UserId { get; set; }

    /// <summary>Window start, minutes from local midnight (0..1439).</summary>
    public int StartMinute { get; set; }

    /// <summary>Window end, minutes from local midnight (0..1439). Less than <see cref="StartMinute"/> means the window crosses midnight.</summary>
    public int EndMinute { get; set; }
}