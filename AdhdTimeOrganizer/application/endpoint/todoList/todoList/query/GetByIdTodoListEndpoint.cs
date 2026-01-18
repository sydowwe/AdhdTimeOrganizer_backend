using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class GetByIdTodoListEndpoint(
    AppCommandDbContext dbContext,
    TodoListMapper mapper)
    : BaseGetByIdEndpoint<TodoList, TodoListResponse, TodoListMapper>(dbContext, mapper)
{
    protected override IQueryable<TodoList> WithIncludes(IQueryable<TodoList> query)
    {
        return query
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Role)
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Category)
            .Include(tdl => tdl.TaskPriority);
    }
}
