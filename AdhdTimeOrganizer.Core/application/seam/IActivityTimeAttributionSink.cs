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
