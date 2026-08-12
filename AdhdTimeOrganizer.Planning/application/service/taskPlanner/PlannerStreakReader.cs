using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using AdhdTimeOrganizer.Planning.domain.service;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.service.taskPlanner;

/// <summary>
/// Turns planner-task rows into the per-day tallies <see cref="PlannerStreakService"/> walks. This class owns
/// the <b>qualifying-task predicate</b> and the <b>day boundary</b>; it owns no rules beyond that, and the
/// rules own no SQL.
/// </summary>
public class PlannerStreakReader(DbContext dbContext) : IPlannerStreakReader, IScopedService
{
    public async Task<PlannerStreakResponse> GetForUserAsync(long userId, CancellationToken ct = default)
    {
        var timezone = await dbContext.Set<User>()
            .Where(u => u.Id == userId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct) ?? TimeZoneInfo.Utc;

        // The day boundary is the user's own midnight, not UTC and not the server's. A Bratislava user
        // ticking their last task at 00:30 has finished *that* day, and a UTC boundary would credit it to the
        // next one and leave a hole in the run. User.Timezone is a real TimeZoneInfo (value-converted from its
        // id), set at registration, so there is nothing to infer and nothing for the client to send.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));

        // One grouped query over the user's whole planning history, deliberately uncapped. A lookback window
        // would make BestStreak a rolling maximum, so a record the user actually set could quietly get smaller
        // over time — worse than the cost it saves. The cost is bounded by *days that have qualifying tasks*,
        // not by task count: a row per planned day, so a few hundred for a heavy user, and
        // (UserId, CalendarId, StartTime) on planner_task covers the scan.
        var rows = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.Calendar.Date <= today
                        // Qualifying tasks only: background tasks are scaffolding, and an optional task is one
                        // the user has already said must not hold them to anything. Both leave the numerator
                        // and the denominator, so a day made only of them is Empty rather than complete.
                        && !t.IsBackground
                        && (t.Importance == null || t.Importance.Importance != TaskImportance.OptionalMarkerValue))
            .GroupBy(t => t.Calendar.Date)
            .Select(g => new
            {
                Date = g.Key,
                Qualifying = g.Count(),
                Cancelled = g.Count(t => t.Status == PlannerTaskStatus.Cancelled),
                Completed = g.Count(t => t.Status == PlannerTaskStatus.Completed)
            })
            .ToListAsync(ct);

        var tallies = rows.Select(r => new PlannerStreakService.PlannerDayTally(r.Date, r.Qualifying, r.Cancelled, r.Completed));
        var snapshot = PlannerStreakService.Walk(tallies, today);

        return new PlannerStreakResponse
        {
            CurrentStreak = snapshot.CurrentStreak,
            BestStreak = snapshot.BestStreak,
            IsTodayComplete = snapshot.IsTodayComplete,
            Today = today,
            Timezone = timezone.Id
        };
    }
}
