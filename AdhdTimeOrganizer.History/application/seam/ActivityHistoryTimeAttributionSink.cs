using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.History.application.seam;

/// <summary>
/// Writes tracked activity time into <see cref="ActivityHistory"/> through Core's seam, so Tracking's
/// heartbeat can attribute time without referencing History.
/// </summary>
/// <remarks>
/// The logic here moved verbatim out of <c>DesktopActivityHeartbeatEndpoint</c>; History is where it
/// belonged all along, since it is the slice that owns the ledger's shape (the contiguous-window
/// extension rule below is a property of <c>ActivityHistory</c>, not of desktop tracking).
/// <para>
/// Every query filters on <c>UserId</c> explicitly even though <c>AppDbContext</c>'s global filter
/// already scopes <see cref="ActivityHistory"/> to the ambient user — the caller passes the id it
/// authenticated, and this must not silently attribute to whoever happens to be ambient.
/// </para>
/// </remarks>
public sealed class ActivityHistoryTimeAttributionSink : IActivityTimeAttributionSink, IScopedService
{
    public async Task AttributeAsync(DbContext db, long userId, long activityId, DateTime windowStart, int activeSeconds, CancellationToken ct)
    {
        var windowEnd = windowStart.AddSeconds(activeSeconds);

        // A heartbeat window that starts exactly where an existing record ends is the same stretch of
        // activity seen across two heartbeats — extend it rather than emitting a second row, or the
        // ledger fragments into one row per heartbeat interval.
        //
        // Never a timer session's row, though: that row is what the timer measured, and its session's outcome
        // describes it. Growing the last focus phase of a Pomodoro past its own length would make the row
        // contradict the session it belongs to — the tracked stretch becomes its own row after it instead.
        var existing = await db.Set<ActivityHistory>()
            .Where(h => h.UserId == userId && h.ActivityId == activityId && h.EndTimestamp == windowStart
                        && h.SessionId == null)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            existing.EndTimestamp = windowEnd;
            existing.Length = new IntTime(existing.Length.TotalSeconds + activeSeconds);
            return;
        }

        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = userId,
            ActivityId = activityId,
            StartTimestamp = windowStart,
            EndTimestamp = windowEnd,
            Length = new IntTime(activeSeconds)
        });
    }

    public async Task<int> TotalSecondsBetweenAsync(DbContext db, long userId, long activityId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Half-open on instants the caller chose — the same contract as TodoListItemLoggedTimeSource, which
        // also leaves "whose day" to its caller. A closed range would count a row starting exactly on the
        // next day's midnight twice, once for each day. The Kind is pinned because Npgsql rejects an
        // Unspecified DateTime against a timestamptz column, and this runs on the live heartbeat path.
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        // Every row that OVERLAPS the range, not only those starting in it. AttributeAsync grows one row across
        // contiguous heartbeats, so a single row can run 09:00–10:30; a start-only predicate counted all of it
        // toward 09:00's range and none toward a range opening at 09:30 — which is exactly the range the
        // completion automation asks for once a same-activity block is done (bugs-to-investigate #2 follow-up).
        // Summed in memory, as it was before the move: Length is an IntTime value object behind a converter,
        // so its TotalSeconds is not translatable to SQL.
        var rows = await db.Set<ActivityHistory>()
            .Where(h => h.UserId == userId && h.ActivityId == activityId
                                           && h.StartTimestamp < to
                                           && (h.EndTimestamp > from || h.StartTimestamp >= from))
            .Select(h => new { h.StartTimestamp, h.EndTimestamp, h.Length })
            .ToListAsync(ct);

        return rows.Sum(r => SecondsInside(r.StartTimestamp, r.EndTimestamp, r.Length.TotalSeconds, from, to));
    }

    /// <summary>
    /// The part of a row's <paramref name="lengthSeconds"/> that falls inside <c>[from, to)</c>, pro rata to the
    /// span clipped. Pro rata rather than the clipped span itself because <c>Length</c> is the ledger's own
    /// figure and is not always the span (a timer session pauses) — this keeps a row wholly inside the range
    /// worth exactly its <c>Length</c>, as it always was. A zero-length row counts where it starts.
    /// </summary>
    private static int SecondsInside(DateTime start, DateTime end, int lengthSeconds, DateTime from, DateTime to)
    {
        var spanSeconds = (end - start).TotalSeconds;
        if (spanSeconds <= 0)
            return start >= from && start < to ? lengthSeconds : 0;

        var clippedStart = start > from ? start : from;
        var clippedEnd = end < to ? end : to;
        var insideSeconds = (clippedEnd - clippedStart).TotalSeconds;
        if (insideSeconds <= 0)
            return 0;

        return insideSeconds >= spanSeconds
            ? lengthSeconds
            : (int)(lengthSeconds * insideSeconds / spanSeconds);
    }
}
