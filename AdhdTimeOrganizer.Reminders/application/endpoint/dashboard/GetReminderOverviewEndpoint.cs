using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// The reminders overview roll-up (GET <c>/reminder-dashboard/overview</c>) — upcoming counts by owner module /
/// kind / next-7-days, recent dispatch outcomes, currently paused/cancelled definitions, and the effective
/// <c>Failed</c>-needing-attention count. Pure aggregate reads over the stored scheduler state + the append-only
/// log; no recomputation. Open to any signed-in user (User/Admin/Root).
/// </summary>
public class GetReminderOverviewEndpoint(DbContext dbContext) : EndpointWithoutRequest<ReminderOverviewResponse>
{
    public override void Configure()
    {
        Get("/reminder-dashboard/overview");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Reminders overview roll-up (admin)");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var next7Days = now.AddDays(7);
        var recentSince = now.AddDays(-7);

        var definitions = dbContext.Set<ReminderDefinition>().AsNoTracking();
        var dispatches = dbContext.Set<ReminderDispatch>().AsNoTracking();

        var upcoming = definitions.Where(x => x.NextOccurrenceAt != null);

        var upcomingByOwnerModule = await upcoming
            .GroupBy(x => x.OwnerModule)
            .Select(g => new ReminderCategoryCount { Key = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count).ThenBy(c => c.Key)
            .ToListAsync(ct);

        var upcomingByKind = await upcoming
            .GroupBy(x => x.Kind)
            .Select(g => new ReminderCategoryCount { Key = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count).ThenBy(c => c.Key)
            .ToListAsync(ct);

        // One aggregate query for both upcoming counts (total + next-7-days).
        var upcomingCounts = await upcoming
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Next7Days = g.Sum(x => x.NextOccurrenceAt <= next7Days ? 1 : 0)
            })
            .FirstOrDefaultAsync(ct);

        var upcomingTotal = upcomingCounts?.Total ?? 0;
        var upcomingNext7Days = upcomingCounts?.Next7Days ?? 0;

        // One grouped query for both status counts (paused + cancelled).
        var statusCounts = await definitions
            .Where(x => x.Status == ReminderStatus.Paused || x.Status == ReminderStatus.Cancelled)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int StatusCount(ReminderStatus s)
        {
            return statusCounts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;
        }

        var pausedCount = StatusCount(ReminderStatus.Paused);
        var cancelledCount = StatusCount(ReminderStatus.Cancelled);

        var recentByOutcome = await dispatches
            .Where(x => x.DispatchedAt >= recentSince)
            .GroupBy(x => x.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Recent(DispatchOutcome o)
        {
            return recentByOutcome.FirstOrDefault(r => r.Outcome == o)?.Count ?? 0;
        }

        // Effective failures: Failed rows not superseded by a later reversal row (the dispatch log is the truth).
        var failedNeedingAttention = await dispatches
            .CountAsync(x => x.Outcome == DispatchOutcome.Failed
                             && !dispatches.Any(r => r.ReversesDispatchId == x.Id), ct);

        await Send.OkAsync(new ReminderOverviewResponse
        {
            UpcomingByOwnerModule = upcomingByOwnerModule,
            UpcomingByKind = upcomingByKind,
            UpcomingNext7Days = upcomingNext7Days,
            UpcomingTotal = upcomingTotal,
            PausedCount = pausedCount,
            CancelledCount = cancelledCount,
            RecentDispatch = new RecentDispatchOutcomeCounts
            {
                Sent = Recent(DispatchOutcome.Sent),
                Skipped = Recent(DispatchOutcome.Skipped),
                Failed = Recent(DispatchOutcome.Failed),
                Reversed = Recent(DispatchOutcome.Reversed)
            },
            FailedNeedingAttention = failedNeedingAttention
        }, ct);
    }
}