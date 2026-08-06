using AdhdTimeOrganizer.Scheduler.application.dashboard;
using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.dashboard.read;

/// <summary>
/// Run history (POST <c>/scheduler-dashboard/run-history</c>): the append-only run log, filtered by job, owner,
/// outcome, trigger source and date range, newest first by default. Shows duration, error type/message,
/// correlation id and replay lineage (<c>ReplaysRunId</c>). Open to any signed-in user (User/Admin/Root).
/// </summary>
public class GetJobRunHistoryEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ScheduledJobRun, ScheduledJobRunDto, JobRunHistoryFilterRequest>(dbContext)
{
    public override void Configure()
    {
        Post("/scheduler-dashboard/run-history");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Dashboard: filtered/paginated append-only run history";
            s.Response<BaseGridResponse<ScheduledJobRunDto>>(200, "Success");
        });
    }

    protected override SortByRequest[] PreprocessSortBy(SortByRequest[] sortBy) => sortBy.Length == 0 ? JobRunHistoryQuery.DefaultSort : sortBy;

    protected override IQueryable<ScheduledJobRun> ApplyCustomFiltering(IQueryable<ScheduledJobRun> query, JobRunHistoryFilterRequest filter) => JobRunHistoryQuery.ApplyFilter(query, filter);
}