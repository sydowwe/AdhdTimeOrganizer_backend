using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.query;

public class GetDashboardTodoListItemEndpoint(AppDbContext dbContext) : BaseGetAllEndpoint<TodoListItem, TodoListItemResponse>(dbContext)
{
    public override string? RouteParam => "dashboard-widget";

    protected override IQueryable<TodoListItem> Filter(IQueryable<TodoListItem> query)
    {
        return query.Where(e => !e.IsDone && e.DueDate.HasValue && e.DueDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(3)));
    }

    protected override IQueryable<TodoListItem> Sort(IQueryable<TodoListItem> query)
    {
        return query.OrderBy(e => e.DueDate);
    }
}