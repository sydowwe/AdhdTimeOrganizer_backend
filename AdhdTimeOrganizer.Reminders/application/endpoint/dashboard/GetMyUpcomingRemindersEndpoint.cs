using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Kernel.reminders;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// The caller's <b>own</b> upcoming reminders (POST <c>/reminder-dashboard/my-upcoming</c>) — recipient
/// self-service, User role and up. <b>Always self-scoped</b> to explicit <c>ReminderRecipient.UserId</c> rows for
/// <c>User.GetId()</c>, so there is no cross-user read path.
/// <para>
/// <b>Resolver-strategy reminders are excluded:</b> their recipients are unknown until dispatch, and a read path
/// must never invoke an <c>IReminderRecipientResolver</c> (resolvers run at dispatch and may reach owner domain
/// code). So the caller only ever sees reminders they're an <em>explicit</em> recipient of.
/// </para>
/// </summary>
public class GetMyUpcomingRemindersEndpoint(DbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<UpcomingReminderFilterRequest>, BaseGridResponse<MyUpcomingReminderDto>>
{
    private static readonly SortByRequest[] DefaultSort = [new() { Key = "nextOccurrenceAt", IsDesc = false }];

    public override void Configure()
    {
        Post("/reminder-dashboard/my-upcoming");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "My upcoming reminders (recipient self-service)");
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<UpcomingReminderFilterRequest> req, CancellationToken ct)
    {
        var userId = User.GetId();

        var query = ReminderDashboardQueries.Upcoming(dbContext)
            .Where(x => x.RecipientMode == RecipientMode.ExplicitUsers && x.Recipients.Any(r => r.UserId == userId));

        if (req is { UseFilter: true, Filter: not null })
            query = ReminderDashboardQueries.ApplyUpcomingFilter(query, req.Filter);

        var sortBy = req.SortBy.Length > 0 ? req.SortBy : DefaultSort;

        var response = await query.GetGridDataAsync(sortBy, req.ItemsPerPage, req.Page, MyUpcomingReminderDto.Projection, ct);
        await Send.OkAsync(response, ct);
    }
}