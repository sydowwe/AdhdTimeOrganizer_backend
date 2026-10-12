using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.seam;

/// <summary>
/// F3-06: what the planner knows about whether an activity happened. Two answers out of one table:
/// <list type="bullet">
/// <item><b>Done</b> — the latest <see cref="PlannerTaskStatus.Completed"/> block, at its actual start when one was
/// recorded and its planned start otherwise. A "do it now" leisure block completes itself at its end, so it lands
/// here without a tick.</item>
/// <item><b>Unkept</b> — past blocks still <see cref="PlannerTaskStatus.NotStarted"/>: planned, and the time went by
/// untouched. That is what a "plan it for later" commitment that did not happen looks like. Cancelled, on-hold and
/// background blocks are not commitments the user broke, so they are not counted.</item>
/// </list>
/// </summary>
/// <remarks>
/// A block's day and times are wall-clock in the user's zone, so both answers are resolved to UTC through it — the
/// leisure ranking compares them with UTC instants from History and from its own record. "Past" is the user's
/// today, not the server's: a block on today's date never counts as missed, however late it is.
/// </remarks>
public sealed class PlannerTaskLastDoneSource(IUserTimeZoneResolver timeZones) : IActivityLastDoneSource, IScopedService
{
    public string Key => ActivityLastDoneSourceKeys.PlannerTask;

    public async Task<IReadOnlyDictionary<long, ActivityDoneEvidence>> ReadAsync(
        DbContext db, long userId, IReadOnlyCollection<long> activityIds, DateTime unkeptSinceUtc, DateTime utcNow,
        CancellationToken ct)
    {
        if (activityIds.Count == 0)
            return new Dictionary<long, ActivityDoneEvidence>();

        var ids = activityIds.Distinct().ToList();
        var zone = await timeZones.GetAsync(userId, ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone));
        var unkeptSince = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(unkeptSinceUtc, zone));

        // One read for both answers. Completed blocks are bounded to a year back — enough for any "last time"
        // the card would say — so a long-lived activity does not drag its whole planning history across.
        var completedSince = today.AddYears(-1);
        var rows = await db.Set<PlannerTask>()
            .Where(t => t.UserId == userId
                        && ids.Contains(t.ActivityId)
                        && !t.IsBackground
                        && t.Calendar.Date <= today
                        && ((t.Status == PlannerTaskStatus.Completed && t.Calendar.Date >= completedSince)
                            || (t.Status == PlannerTaskStatus.NotStarted && t.Calendar.Date < today
                                                                         && t.Calendar.Date >= unkeptSince)))
            .Select(t => new { t.ActivityId, t.Status, t.Calendar.Date, t.StartTime, t.ActualStartTime })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ActivityId)
            .ToDictionary(g => g.Key, g =>
            {
                var done = g.Where(r => r.Status == PlannerTaskStatus.Completed)
                    .Select(r => ToUtc(r.Date, r.ActualStartTime ?? r.StartTime, zone))
                    .Where(at => at < utcNow)
                    .DefaultIfEmpty()
                    .Max();
                var unkept = g.Where(r => r.Status == PlannerTaskStatus.NotStarted)
                    .Select(r => ToUtc(r.Date, r.StartTime, zone))
                    .Where(at => at >= unkeptSinceUtc)
                    .OrderBy(at => at)
                    .ToList();
                return new ActivityDoneEvidence(done == default ? null : done, unkept);
            });
    }

    /// <summary>
    /// A block planned inside a spring-forward gap names a wall-clock time that never happened, and
    /// <c>ConvertTimeToUtc</c> throws on it; the hour after is the same moment the clocks jumped to. Same
    /// handling as <c>ReminderRegistrationService</c>. Day-level precision is all the ranking reads anyway.
    /// </summary>
    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
