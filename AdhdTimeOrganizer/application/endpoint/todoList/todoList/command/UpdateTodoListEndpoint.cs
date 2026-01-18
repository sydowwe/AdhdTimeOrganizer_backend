using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class UpdateTodoListEndpoint(AppCommandDbContext dbContext, TodoListMapper mapper)
    : BaseUpdateEndpoint<TodoList, UpdateTodoListRequest, TodoListMapper>(dbContext, mapper)
{
    protected override Task AfterMapping(TodoList entity, UpdateTodoListRequest req, CancellationToken ct = default)
    {
        if (req is { TotalCount: not null, DoneCount: null })
        {
            entity.DoneCount = 0;
        }

        return Task.CompletedTask;
    }
}
