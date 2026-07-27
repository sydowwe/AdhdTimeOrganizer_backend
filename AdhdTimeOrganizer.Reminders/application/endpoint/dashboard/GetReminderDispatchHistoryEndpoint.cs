using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using AdhdTimeOrganizer.Reminders.application.dto.reminderDispatch;
using AdhdTimeOrganizer.Reminders.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// The append-only dispatch log (POST <c>/reminder-dashboard/dispatch-history</c>) â€” a straight projection of the
/// ledger, <b>reversals included</b>, paged / filtered / sorted by reminder, owner, recipient, outcome and
/// dispatched-at range. Shows the skip/fail reason + the immutable recipient snapshot. The dispatch log is the
/// audit trail / source of truth. Open to any signed-in user (User/Admin/Root). Default order: most recently dispatched first.
/// </summary>
public class GetReminderDispatchHistoryEndpoint(DbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<ReminderDispatchHistoryFilterRequest>, BaseGridResponse<ReminderDispatchDto>>
{
    private static readonly SortByRequest[] DefaultSort = [new() { Key = "dispatchedAt", IsDesc = true }];

    public override void Configure()
    {
        Post("/reminder-dashboard/dispatch-history");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Reminder dispatch history / audit log (admin)");
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<ReminderDispatchHistoryFilterRequest> req, CancellationToken ct)
    {
        var query = dbContext.Set<ReminderDispatch>().AsNoTracking();

        if (req is { UseFilter: true, Filter: not null })
            query = ReminderDashboardQueries.ApplyDispatchHistoryFilter(query, req.Filter);

        var sortBy = req.SortBy.Length > 0 ? req.SortBy : DefaultSort;

        var response = await query.GetGridDataAsync(sortBy, req.ItemsPerPage, req.Page, ReminderDispatchDto.Projection, ct);
        await Send.OkAsync(response, ct);
    }
}