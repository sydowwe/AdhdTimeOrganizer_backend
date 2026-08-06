using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.scheduledJob.query;

/// <summary>
/// Registered-jobs grid (POST /scheduled-job/filtered-table). Open to any signed-in user (User/Admin/Root)
/// via the base default. The <c>ApplyUserScoping</c> no-op stays correct here: the job registry is
/// infrastructure with no per-user rows and no PII.
/// </summary>
public class GridScheduledJobEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ScheduledJob, ScheduledJobDto, ScheduledJobFilterRequest>(dbContext)
{
    protected override IQueryable<ScheduledJob> ApplyCustomFiltering(IQueryable<ScheduledJob> query, ScheduledJobFilterRequest filter)
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

        return query;
    }
}