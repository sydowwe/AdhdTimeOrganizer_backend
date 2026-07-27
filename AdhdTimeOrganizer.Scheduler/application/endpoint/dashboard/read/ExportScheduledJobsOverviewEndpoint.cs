using AdhdTimeOrganizer.Scheduler.application.dashboard;
using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.application.export;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.dashboard.read;

/// <summary>
/// Jobs-overview export (POST <c>/scheduler-dashboard/jobs-overview/export?format=xlsx|csv</c>). Same filter as
/// the grid (shared <see cref="ScheduledJobsOverviewQuery"/>), no paging â€” the whole filtered set. Open to any signed-in user (User/Admin/Root).
/// </summary>
public class ExportScheduledJobsOverviewEndpoint(DbContext dbContext, ISchedulerExportService exportService)
    : Endpoint<ScheduledJobsOverviewFilterRequest>
{
    public override void Configure()
    {
        Post("/scheduler-dashboard/jobs-overview/export");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Dashboard: export the registered-jobs overview (xlsx/csv)");
    }

    public override async Task HandleAsync(ScheduledJobsOverviewFilterRequest req, CancellationToken ct)
    {
        var format = SchedulerExportFormatParser.TryParse(Query<string?>("format", false));
        if (format is null)
        {
            AddError("Unsupported export format. Use 'xlsx' or 'csv'.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var filtered = ScheduledJobsOverviewQuery.ApplyFilter(dbContext.Set<ScheduledJob>().AsNoTracking(), req, DateTime.UtcNow);
        var jobs = await ScheduledJobDto.Projection(filtered).SortByMany(ScheduledJobsOverviewQuery.DefaultSort).ToListAsync(ct);

        var file = exportService.ExportJobsOverview(jobs, format.Value);
        await Send.BytesAsync(file.Content, file.FileName, file.ContentType, cancellation: ct);
    }
}