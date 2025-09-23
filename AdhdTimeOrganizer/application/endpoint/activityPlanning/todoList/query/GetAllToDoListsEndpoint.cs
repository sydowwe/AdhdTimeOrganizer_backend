using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.todoList.query;

public class GetAllTodoListsEndpoint(
    AppCommandDbContext dbContext,
    TodoListMapper mapper)
    : BaseGetAllEndpoint<TodoList, TodoListResponse, TodoListMapper>(dbContext, mapper)
{
    protected override IQueryable<TodoList> WithIncludes(IQueryable<TodoList> query)
    {
        return query
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Role)
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Category)
            .Include(tdl => tdl.TaskUrgency);
    }

    protected override IQueryable<TodoList> Sort(IQueryable<TodoList> query)
    {
        return query.OrderBy(td=>td.DisplayOrder);
    }
}
