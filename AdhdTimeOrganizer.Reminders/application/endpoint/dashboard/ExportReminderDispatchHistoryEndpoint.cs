using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using AdhdTimeOrganizer.Reminders.application.dto.reminderDispatch;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// Downloads the dispatch-history ledger as a file (<c>?format=csv</c>, default csv). Reuses the exact same
/// filtered query as <see cref="GetReminderDispatchHistoryEndpoint"/> so the file can never drift from the API.
/// Open to any signed-in user (User/Admin/Root). Ordered most-recent-first, no paging (whole result set), reversals included.
/// </summary>
public class ExportReminderDispatchHistoryEndpoint(DbContext dbContext, IReminderExportService exportService)
    : BaseReminderExportEndpoint<BaseFilterRequest<ReminderDispatchHistoryFilterRequest>>
{
    public override void Configure()
    {
        Post("/reminder-dashboard/dispatch-history/export");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Export reminder dispatch history (CSV)");
    }

    public override async Task HandleAsync(BaseFilterRequest<ReminderDispatchHistoryFilterRequest> req, CancellationToken ct)
    {
        var format = await ResolveFormatOrFailAsync(ct);
        if (format is null)
            return;

        var query = dbContext.Set<ReminderDispatch>().AsNoTracking();
        if (req is { UseFilter: true, Filter: not null })
            query = ReminderDashboardQueries.ApplyDispatchHistoryFilter(query, req.Filter);

        var rows = await ReminderDispatchDto.Projection(query.OrderByDescending(x => x.DispatchedAt)).ToListAsync(ct);

        var file = exportService.ExportDispatchHistory(rows, format.Value);
        await Send.BytesAsync(file.Content, file.FileName, file.ContentType, cancellation: ct);
    }
}