using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.query;

public class GetByIdTodoListItemEndpoint(AppDbContext dbContext, TodoListItemMapper mapper)
    : BaseGetByIdEndpoint<TodoListItem, TodoListItemResponse, TodoListItemMapper>(dbContext, mapper)
{
    protected override IQueryable<TodoListItem> WithIncludes(IQueryable<TodoListItem> query)
    {
        return query
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Role)
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Category)
            .Include(tdl => tdl.TaskPriority);
    }
}
