using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using Sydowwe.Framework.application.dto.request.generic;

namespace AdhdTimeOrganizer.Scheduler.application.dashboard;

/// <summary>
/// Shared filter + default sort for the run-history surfaces, so the grid and its <c>Export*</c> variant can
/// never diverge. Owner is filtered through the <c>ScheduledJob</c> navigation (the run row snapshots only the
/// keys); everything else is on the run row itself.
/// </summary>
public static class JobRunHistoryQuery
{
    /// <summary>Default order when the caller sends none: newest first (the natural log view).</summary>
    public static readonly SortByRequest[] DefaultSort =
    [
        new() { Key = nameof(ScheduledJobRun.StartedAt), IsDesc = true }
    ];

    public static IQueryable<ScheduledJobRun> ApplyFilter(IQueryable<ScheduledJobRun> query, JobRunHistoryFilterRequest filter)
    {
        if (filter.ScheduledJobId.HasValue)
            query = query.Where(x => x.ScheduledJobId == filter.ScheduledJobId.Value);

        if (!string.IsNullOrWhiteSpace(filter.JobKey))
            query = query.Where(x => x.JobKeySnapshot == filter.JobKey);

        if (!string.IsNullOrWhiteSpace(filter.OwnerModule))
            query = query.Where(x => x.ScheduledJob.OwnerModule == filter.OwnerModule);

        if (filter.Outcome.HasValue)
            query = query.Where(x => x.Outcome == filter.Outcome.Value);

        if (filter.TriggerSource.HasValue)
            query = query.Where(x => x.TriggerSource == filter.TriggerSource.Value);

        if (filter.StartedFrom.HasValue)
            query = query.Where(x => x.StartedAt >= filter.StartedFrom.Value);

        if (filter.StartedTo.HasValue)
            query = query.Where(x => x.StartedAt <= filter.StartedTo.Value);

        return query;
    }
}