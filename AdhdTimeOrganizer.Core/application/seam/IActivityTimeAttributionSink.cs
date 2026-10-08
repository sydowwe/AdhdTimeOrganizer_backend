using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Records "this activity was observed for N seconds over this window" into whatever slice owns the
/// activity time ledger, without the caller naming that slice.
/// </summary>
/// <remarks>
/// <para>
/// Tracking's desktop heartbeat attributes matched tracked time to an activity. That attribution
/// lands in <c>ActivityHistory</c>, which belongs to History — so writing it directly is what used to
/// force a Tracking → History project reference. History implements this; Tracking depends on Core
/// alone.
/// </para>
/// <para>
/// ⚠ <b>Implementations mutate the context handed in and must not call <c>SaveChanges</c>.</b> The
/// caller's own save is the transaction, which is what keeps the attribution atomic with the raw
/// tracking rows it was derived from — the same contract <c>ISubjectDataEraser</c> uses for account
/// deletion. An implementation that saves splits one commit into two and can leave tracked entries
/// with no attribution if the second one fails.
/// </para>
/// <para>
/// Unlike <see cref="IActivityMembershipSource"/> this is resolved as a <em>single</em> service, not
/// a keyed <c>IEnumerable</c>. That is deliberate: a missing registration throws at endpoint
/// activation rather than silently dropping every attribution write.
/// </para>
/// </remarks>
public interface IActivityTimeAttributionSink : ISeam
{
    /// <summary>
    /// Attributes <paramref name="activeSeconds"/> to <paramref name="activityId"/> for the window
    /// starting at <paramref name="windowStart"/>, extending a contiguous preceding record where one
    /// exists rather than fragmenting the ledger.
    /// </summary>
    /// <param name="db">The ambient context. Entities are added/updated on it and left unsaved.</param>
    Task AttributeAsync(DbContext db, long userId, long activityId, DateTime windowStart, int activeSeconds, CancellationToken ct);

    /// <summary>
    /// Attributes the part of <c>[<paramref name="startUtc"/>, <paramref name="endUtc"/>)</c> that no record of
    /// <paramref name="activityId"/> already covers, and returns how many seconds that was. The Android sync's
    /// entry point (FO9): a phone session is a span, not a one-minute window, and it usually arrives after the
    /// desktop heartbeat or a timer has already recorded some of the same stretch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same activity is never counted twice for the same second.</b> Phone reading mapped to
    /// <i>Thesis</i> during a <i>Thesis</i> pomodoro adds only what the pomodoro did not cover, so the day total
    /// the completion automation reads stays the time the user actually spent. Records of <em>other</em>
    /// activities are not subtracted: overlapping rows of different activities are already a shape the ledger
    /// holds (hand-logged rows), and choosing between two sources' verdicts is the unified dashboards' job.
    /// </para>
    /// <para>
    /// Records still unsaved on <paramref name="db"/> count as covering, so two sessions of one batch that
    /// overlap (two devices, or two packages mapped to one activity) are attributed once. A remaining piece that
    /// starts where a non-timer record ends extends that record, as <see cref="AttributeAsync"/> does. A piece
    /// that extends nothing and is shorter than a minute is dropped, because the ledger would store it as a
    /// zero-length row (<c>IntTime</c> keeps whole minutes, a pinned <c>KnownGap</c>).
    /// </para>
    /// <para>The no-<c>SaveChanges</c> contract above applies unchanged.</para>
    /// </remarks>
    Task<int> AttributeSpanAsync(DbContext db, long userId, long activityId, DateTime startUtc, DateTime endUtc, CancellationToken ct);

    /// <summary>
    /// Total seconds already attributed to <paramref name="activityId"/> in the half-open instant range
    /// <c>[<paramref name="fromUtc"/>, <paramref name="toUtc"/>)</c>, across every source — not just the one
    /// that produced the current batch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read <em>after</em> the caller has saved, otherwise the batch being processed is not counted.
    /// </para>
    /// <para>
    /// A record counts by <b>overlap</b>, pro rata: one stretch of activity is a single record however long it
    /// runs, so a record straddling a bound contributes only the part inside the range. That is what lets a
    /// caller ask for "time since 09:30" and get the minutes after 09:30 of a record that began at 09:00.
    /// </para>
    /// <para>
    /// The caller supplies the range, because the sink cannot know whose day it is. For "the user's day"
    /// that means the user's own midnights converted to UTC (<c>WallClockZone.ToUtc</c>) — the same shape
    /// <see cref="ITodoListItemLoggedTimeSource"/> takes. This used to take a bare <see cref="DateOnly"/>
    /// bounded on UTC midnights, so for a Bratislava user the heartbeat's "day" ran 01:00/02:00 to
    /// 01:00/02:00 local, and time tracked just after their midnight drove yesterday's planner task.
    /// </para>
    /// </remarks>
    Task<int> TotalSecondsBetweenAsync(DbContext db, long userId, long activityId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
