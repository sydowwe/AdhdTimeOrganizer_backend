using AdhdTimeOrganizer.Scheduler.application.dashboard;
using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.dashboard.read;

/// <summary>
/// Dashboard jobs overview (POST <c>/scheduler-dashboard/jobs-overview</c>): every registered job with its
/// schedule, status, last/next run + last outcome. Reuses the phase-01 <see cref="ScheduledJobDto"/> projection
/// and the shared grid base; adds over the 02b diagnostic grid only the last-outcome + overdue filters and a
/// stable default order. Open to any signed-in user (User/Admin/Root); the <c>ApplyUserScoping</c> no-op stays
/// correct because the job registry has no per-user rows.
/// </summary>
public class GetScheduledJobsOverviewEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ScheduledJob, ScheduledJobDto, ScheduledJobsOverviewFilterRequest>(dbContext)
{
    public override void Configure()
    {
        Post("/scheduler-dashboard/jobs-overview");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Dashboard: filtered/paginated registered-jobs overview";
            s.Response<BaseGridResponse<ScheduledJobDto>>(200, "Success");
        });
    }

    protected override SortByRequest[] PreprocessSortBy(SortByRequest[] sortBy) => sortBy.Length == 0 ? ScheduledJobsOverviewQuery.DefaultSort : sortBy;

    protected override IQueryable<ScheduledJob> ApplyCustomFiltering(IQueryable<ScheduledJob> query, ScheduledJobsOverviewFilterRequest filter) =>
        ScheduledJobsOverviewQuery.ApplyFilter(query, filter, DateTime.UtcNow);
}