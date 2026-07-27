using AdhdTimeOrganizer.Scheduler.application.dashboard;
using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;
using AdhdTimeOrganizer.Scheduler.application.export;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.dashboard.read;

/// <summary>
/// Run-history export (POST <c>/scheduler-dashboard/run-history/export?format=xlsx|csv</c>). Same filter as the
/// grid (shared <see cref="JobRunHistoryQuery"/>), newest first, no paging. The payload snapshot is deliberately
/// never exported (potential PII). Open to any signed-in user (User/Admin/Root).
/// </summary>
public class ExportJobRunHistoryEndpoint(DbContext dbContext, ISchedulerExportService exportService)
    : Endpoint<JobRunHistoryFilterRequest>
{
    public override void Configure()
    {
        Post("/scheduler-dashboard/run-history/export");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Dashboard: export the run history (xlsx/csv)");
    }

    public override async Task HandleAsync(JobRunHistoryFilterRequest req, CancellationToken ct)
    {
        var format = SchedulerExportFormatParser.TryParse(Query<string?>("format", false));
        if (format is null)
        {
            AddError("Unsupported export format. Use 'xlsx' or 'csv'.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var filtered = JobRunHistoryQuery.ApplyFilter(dbContext.Set<ScheduledJobRun>().AsNoTracking(), req);
        var runs = await ScheduledJobRunDto.Projection(filtered).SortByMany(JobRunHistoryQuery.DefaultSort).ToListAsync(ct);

        var file = exportService.ExportRunHistory(runs, format.Value);
        await Send.BytesAsync(file.Content, file.FileName, file.ContentType, cancellation: ct);
    }
}