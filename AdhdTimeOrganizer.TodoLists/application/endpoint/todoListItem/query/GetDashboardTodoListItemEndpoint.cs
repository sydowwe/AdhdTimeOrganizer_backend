using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.application.service;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.query;

/// <summary>
/// The widget's short horizon: everything still open that is due within the next
/// <see cref="TodoListSettings.DashboardHorizonDays"/> days.
/// </summary>
/// <remarks>
/// The window closes on the user's own date, resolved through <see cref="IUserTimeZoneResolver"/>, the way
/// every other dashboard in this solution bounds a day. A server-local <c>DateTime.Today</c> would drop a
/// task on its due date for a user east of the server and show one a day early for a user west of it.
/// </remarks>
public class GetDashboardTodoListItemEndpoint(
    DbContext dbContext,
    IUserTimeZoneResolver timeZones,
    IOptions<TodoListSettings> settings,
    ITodoListItemPlanSource planSource)
    : BaseGetAllEndpoint<TodoListItem, TodoListItemResponse>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    // Filter is synchronous by contract and resolving the caller's zone is a database read, so the horizon
    // is computed in HandleAsync before the base builds the query. Only ever read on the line after it is
    // written, within one request.
    private DateOnly _horizonEnd;

    public override string? RouteParam => "dashboard-widget";

    public override async Task HandleAsync(CancellationToken ct)
    {
        var timeZone = await timeZones.GetAsync(User.GetId(), ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));

        _horizonEnd = today.AddDays(settings.Value.DashboardHorizonDays);

        await base.HandleAsync(ct);
    }

    protected override IQueryable<TodoListItem> Filter(IQueryable<TodoListItem> query)
    {
        var horizonEnd = _horizonEnd;
        return query.Where(e => !e.IsDone && e.DueDate.HasValue && e.DueDate.Value <= horizonEnd);
    }

    protected override IQueryable<TodoListItem> Sort(IQueryable<TodoListItem> query)
    {
        return query.OrderBy(e => e.DueDate);
    }

    /// <summary>FO3: the home widget offers the same help on a stuck item as the list does.</summary>
    protected override Task<List<TodoListItemResponse>> PostProcessItemsAsync(List<TodoListItemResponse> items, CancellationToken ct) =>
        TodoListItemPlannedSlotFiller.FillPlanHelpAsync(items, _dbContext, planSource, timeZones, User.GetId(), ct);
}
