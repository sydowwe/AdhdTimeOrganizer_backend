using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.History.application.seam;

/// <summary>
/// F3-06: the latest recording's start per activity. Every recording counts — a timer, a manual entry, a
/// tracker-attributed block — because each one is time the user spent on the activity. Keyed on
/// <c>ActivityId</c>, deliberately: "did this happen" is about the activity, unlike the per-item logged-time
/// seam, where widening to the activity double-counts.
/// </summary>
public sealed class ActivityHistoryLastDoneSource : IActivityLastDoneSource, IScopedService
{
    public string Key => ActivityLastDoneSourceKeys.ActivityHistory;

    public async Task<IReadOnlyDictionary<long, ActivityDoneEvidence>> ReadAsync(
        DbContext db, long userId, IReadOnlyCollection<long> activityIds, DateTime unkeptSinceUtc, DateTime utcNow,
        CancellationToken ct)
    {
        if (activityIds.Count == 0)
            return new Dictionary<long, ActivityDoneEvidence>();

        var ids = activityIds.Distinct().ToList();

        // Grouped in SQL: a tracked activity can carry thousands of rows, and only the newest start matters.
        // Scoped on the caller's UserId explicitly rather than on the ambient filter.
        var latest = await db.Set<ActivityHistory>()
            .Where(h => h.UserId == userId && ids.Contains(h.ActivityId) && h.StartTimestamp < utcNow)
            .GroupBy(h => h.ActivityId)
            .Select(g => new { ActivityId = g.Key, LastStart = g.Max(h => h.StartTimestamp) })
            .ToListAsync(ct);

        return latest.ToDictionary(
            row => row.ActivityId,
            row => new ActivityDoneEvidence(DateTime.SpecifyKind(row.LastStart, DateTimeKind.Utc), ActivityDoneEvidence.NoneUnkept));
    }
}
