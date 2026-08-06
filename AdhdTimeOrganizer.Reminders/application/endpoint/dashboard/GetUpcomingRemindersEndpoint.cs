using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// Upcoming (not-yet-fired) reminders across all modules (POST <c>/reminder-dashboard/upcoming</c>), paged /
/// filtered / sorted. <b>One row per active definition, showing its single soonest <c>NextOccurrenceAt</c></b> —
/// later occurrences of recurring / multi-offset reminders are deliberately <b>not</b> expanded (that would be
/// recomputation and isn't SQL-translatable). Open to any signed-in user (User/Admin/Root), and it spans EVERY
/// user's reminders with no user scoping — acceptable single-user, a leak once this app is multi-tenant; the
/// self-service view is <c>GetMyUpcomingRemindersEndpoint</c>. Default order: soonest first.
/// </summary>
public class GetUpcomingRemindersEndpoint(DbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<UpcomingReminderFilterRequest>, BaseGridResponse<ReminderDefinitionDto>>
{
    private static readonly SortByRequest[] DefaultSort = [new() { Key = "nextOccurrenceAt", IsDesc = false }];

    public override void Configure()
    {
        Post("/reminder-dashboard/upcoming");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Upcoming reminders across all modules (admin)");
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<UpcomingReminderFilterRequest> req, CancellationToken ct)
    {
        var query = ReminderDashboardQueries.Upcoming(dbContext);

        if (req is { UseFilter: true, Filter: not null })
            query = ReminderDashboardQueries.ApplyUpcomingFilter(query, req.Filter);

        var sortBy = req.SortBy.Length > 0 ? req.SortBy : DefaultSort;

        var response = await query.GetGridDataAsync(sortBy, req.ItemsPerPage, req.Page, ReminderDefinitionDto.Projection, ct);
        await Send.OkAsync(response, ct);
    }
}