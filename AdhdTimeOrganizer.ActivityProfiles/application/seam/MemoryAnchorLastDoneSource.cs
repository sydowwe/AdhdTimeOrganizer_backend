using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.application.seam;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.ActivityProfiles.application.seam;

/// <summary>
/// F3-06: a memory anchor says the activity happened in a month, so the latest anchored month is positive
/// evidence. It is read as the <b>first</b> of that month: the anchor proves the activity happened on or after
/// that day and claims nothing more precise, and evidence must never overstate how recent something was.
/// </summary>
/// <remarks>
/// This slice both implements and consumes the seam — the leisure draw is its own reader — but it goes through
/// the seam anyway, so the draw merges all three sources the same way and none of them is special-cased.
/// </remarks>
public sealed class MemoryAnchorLastDoneSource : IActivityLastDoneSource, IScopedService
{
    public string Key => ActivityLastDoneSourceKeys.MemoryAnchor;

    public async Task<IReadOnlyDictionary<long, ActivityDoneEvidence>> ReadAsync(
        DbContext db, long userId, IReadOnlyCollection<long> activityIds, DateTime unkeptSinceUtc, DateTime utcNow,
        CancellationToken ct)
    {
        if (activityIds.Count == 0)
            return new Dictionary<long, ActivityDoneEvidence>();

        var ids = activityIds.Distinct().ToList();

        var latest = await db.Set<MemoryAnchor>()
            .Where(a => a.UserId == userId && ids.Contains(a.ActivityId))
            .GroupBy(a => a.ActivityId)
            .Select(g => new { ActivityId = g.Key, Period = g.Max(a => a.AnchorYear * 100 + a.AnchorMonth) })
            .ToListAsync(ct);

        return latest
            .Select(row => (row.ActivityId, At: new DateTime(row.Period / 100, row.Period % 100, 1, 0, 0, 0, DateTimeKind.Utc)))
            // An anchor for a month that has not started is not evidence of anything yet.
            .Where(row => row.At < utcNow)
            .ToDictionary(row => row.ActivityId, row => new ActivityDoneEvidence(row.At, ActivityDoneEvidence.NoneUnkept));
    }
}
