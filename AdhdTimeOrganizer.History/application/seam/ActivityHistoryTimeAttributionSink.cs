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
        var existing = await db.Set<ActivityHistory>()
            .Where(h => h.UserId == userId && h.ActivityId == activityId && h.EndTimestamp == windowStart)
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

    public async Task<int> TotalSecondsOnDayAsync(DbContext db, long userId, long activityId, DateOnly day, CancellationToken ct)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var dayEnd = day.ToDateTime(TimeOnly.MaxValue);

        // Summed in memory, as it was before the move: Length is an IntTime value object behind a
        // converter, so its TotalSeconds is not translatable to SQL.
        var lengths = await db.Set<ActivityHistory>()
            .Where(h => h.UserId == userId && h.ActivityId == activityId
                                           && h.StartTimestamp >= dayStart && h.StartTimestamp <= dayEnd)
            .Select(h => h.Length)
            .ToListAsync(ct);

        return lengths.Sum(l => l.TotalSeconds);
    }
}
