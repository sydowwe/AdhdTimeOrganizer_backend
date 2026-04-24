using System.Linq.Expressions;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper.todoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.query;

public class GetTodoListItemsForDashboardEndpoint(AppDbContext dbContext, TodoListItemMapper mapper) : BaseGetAllEndpoint<TodoListItem, TodoListItemResponse, TodoListItemMapper>(dbContext, mapper)
{
    public override string? RouteParam => "dashboard-widget";
    protected override Expression<Func<TodoListItem, bool>> FilterQuery => e => !e.IsDone && e.DueDate.HasValue && e.DueDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(3));
    protected override IQueryable<TodoListItem> Sort(IQueryable<TodoListItem> query)
    {
        return query.OrderBy(e => e.DueDate);
    }
}