using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class UpdateTodoListItemEndpoint(AppDbContext dbContext, TodoListItemMapper mapper)
    : BaseUpdateEndpoint<TodoListItem, UpdateTodoListItemRequest, TodoListItemMapper>(dbContext, mapper)
{
    protected override Task AfterMapping(TodoListItem entity, UpdateTodoListItemRequest req, CancellationToken ct = default)
    {
        if (req is { TotalCount: not null, DoneCount: null })
        {
            entity.DoneCount = 0;
        }

        return Task.CompletedTask;
    }
}
