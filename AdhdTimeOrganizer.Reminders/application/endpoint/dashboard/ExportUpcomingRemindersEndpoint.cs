using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using AdhdTimeOrganizer.Reminders.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// Downloads the upcoming-reminders list as a file (<c>?format=csv</c>, default csv). Reuses the exact same
/// filtered query as <see cref="GetUpcomingRemindersEndpoint"/> â€” only the rendering differs â€” so the file can
/// never drift from the API. Open to any signed-in user (User/Admin/Root). Ordered soonest-first, no paging (whole result set).
/// </summary>
public class ExportUpcomingRemindersEndpoint(DbContext dbContext, IReminderExportService exportService)
    : BaseReminderExportEndpoint<BaseFilterRequest<UpcomingReminderFilterRequest>>
{
    public override void Configure()
    {
        Post("/reminder-dashboard/upcoming/export");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Export upcoming reminders (CSV)");
    }

    public override async Task HandleAsync(BaseFilterRequest<UpcomingReminderFilterRequest> req, CancellationToken ct)
    {
        var format = await ResolveFormatOrFailAsync(ct);
        if (format is null)
            return;

        var query = ReminderDashboardQueries.Upcoming(dbContext);
        if (req is { UseFilter: true, Filter: not null })
            query = ReminderDashboardQueries.ApplyUpcomingFilter(query, req.Filter);

        var rows = await ReminderDefinitionDto.Projection(query.OrderBy(x => x.NextOccurrenceAt)).ToListAsync(ct);

        var file = exportService.ExportUpcoming(rows, format.Value);
        await Send.BytesAsync(file.Content, file.FileName, file.ContentType, cancellation: ct);
    }
}