using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using Sydowwe.Framework.application.dto.request.generic;

namespace AdhdTimeOrganizer.Scheduler.application.dashboard;

/// <summary>
/// Shared filter + default sort for the jobs-overview surfaces, so the grid and its <c>Export*</c> variant can
/// never diverge. Mirrors the 02b diagnostic-grid filter and adds the dashboard-only last-outcome + overdue signals.
/// </summary>
public static class ScheduledJobsOverviewQuery
{
    /// <summary>Stable default order when the caller sends none: by owner, then job key.</summary>
    public static readonly SortByRequest[] DefaultSort =
    [
        new() { Key = nameof(ScheduledJob.OwnerModule), IsDesc = false },
        new() { Key = nameof(ScheduledJob.JobKey), IsDesc = false }
    ];

    public static IQueryable<ScheduledJob> ApplyFilter(IQueryable<ScheduledJob> query, ScheduledJobsOverviewFilterRequest filter, DateTime utcNow)
    {
        if (!string.IsNullOrWhiteSpace(filter.OwnerModule))
            query = query.Where(x => x.OwnerModule == filter.OwnerModule);

        if (!string.IsNullOrWhiteSpace(filter.HandlerKey))
            query = query.Where(x => x.HandlerKey == filter.HandlerKey);

        if (filter.Status.HasValue)
            query = query.Where(x => x.Status == filter.Status.Value);

        if (filter.ScheduleType.HasValue)
            query = query.Where(x => x.ScheduleType == filter.ScheduleType.Value);

        if (filter.NextRunFrom.HasValue)
            query = query.Where(x => x.NextRunAt >= filter.NextRunFrom.Value);

        if (filter.NextRunTo.HasValue)
            query = query.Where(x => x.NextRunAt <= filter.NextRunTo.Value);

        if (filter.LastOutcome.HasValue)
            query = query.Where(x => x.LastOutcome == filter.LastOutcome.Value);

        if (filter.OnlyOverdue == true)
            query = query.WhereOverdue(utcNow);

        return query;
    }
}