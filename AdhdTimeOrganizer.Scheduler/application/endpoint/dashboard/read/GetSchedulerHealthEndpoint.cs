using AdhdTimeOrganizer.Scheduler.application.dashboard;
using AdhdTimeOrganizer.Scheduler.application.dto.health;
using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.@enum;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.dashboard.read;

/// <summary>
/// Scheduler health (GET <c>/scheduler-dashboard/health</c>): the "needs attention" view. Status counts, recent
/// run outcomes over a 24h window, and the three actionable lists — failed last run, overdue (Active past the
/// grace margin), and orphaned (handler no longer registered). The run log is the source of truth: the recent
/// outcomes come straight off it. Open to any signed-in user (User/Admin/Root).
/// </summary>
public class GetSchedulerHealthEndpoint(DbContext dbContext, IEnumerable<IScheduledJobHandler> handlers)
    : EndpointWithoutRequest<SchedulerHealthDto>
{
    private const int RecentWindowHours = 24;

    public override void Configure()
    {
        Get("/scheduler-dashboard/health");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Dashboard: scheduler health — failed / overdue / orphaned jobs");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var jobs = dbContext.Set<ScheduledJob>().AsNoTracking();
        var runs = dbContext.Set<ScheduledJobRun>().AsNoTracking();

        var statusCounts = await jobs
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var recentOutcomes = await runs
            .Where(x => x.StartedAt >= now.AddHours(-RecentWindowHours))
            .GroupBy(x => x.Outcome)
            .Select(g => new RunOutcomeCountDto { Outcome = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var failedJobs = await ScheduledJobDto
            .Projection(jobs.Where(x => x.LastOutcome == RunOutcome.Failed))
            .ToListAsync(ct);

        var overdueJobs = await ScheduledJobDto
            .Projection(jobs.WhereOverdue(now))
            .ToListAsync(ct);

        // Orphaned = a still-relevant registry row whose handler key no longer resolves to a registered handler
        // (its owning module was removed/renamed). A deliberately Removed job is excluded — it is not actionable.
        // The key set is small and in-memory, so EF translates the negated Contains to a SQL NOT IN — no need
        // to materialize every live job and filter on the request thread.
        var registeredKeys = handlers.Select(h => h.Key).ToList();
        var orphanedJobs = await ScheduledJobDto
            .Projection(jobs.Where(x => x.Status != JobStatus.Removed && !registeredKeys.Contains(x.HandlerKey)))
            .ToListAsync(ct);

        await Send.OkAsync(new SchedulerHealthDto
        {
            ActiveCount = statusCounts.FirstOrDefault(c => c.Status == JobStatus.Active)?.Count ?? 0,
            PausedCount = statusCounts.FirstOrDefault(c => c.Status == JobStatus.Paused)?.Count ?? 0,
            RemovedCount = statusCounts.FirstOrDefault(c => c.Status == JobStatus.Removed)?.Count ?? 0,
            RecentWindowHours = RecentWindowHours,
            RecentRunOutcomes = recentOutcomes,
            FailedJobs = failedJobs,
            OverdueJobs = overdueJobs,
            OrphanedJobs = orphanedJobs
        }, ct);
    }
}