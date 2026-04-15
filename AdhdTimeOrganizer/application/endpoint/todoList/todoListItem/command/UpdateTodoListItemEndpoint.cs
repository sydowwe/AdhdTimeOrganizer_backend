using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListItemMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListItemMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class UpdateTodoListItemEndpoint(AppDbContext dbContext, TodoListItemMapper mapper)
    : BaseUpdateEndpoint<TodoListItem, UpdateTodoListItemRequest, TodoListItemMapper>(dbContext, mapper)
{
    protected override Task AfterMapping(TodoListItem entity, UpdateTodoListItemRequest req, CancellationToken ct = default)
    {
        if (req.Steps is not null)
        {
            entity.Steps = req.Steps.Select(s => new TodoListStep
            {
                Name = s.Name,
                Order = s.Order,
                Note = s.Note,
                IsDone = s.Id.HasValue && entity.Steps.FirstOrDefault(e => e.Id == s.Id.Value)?.IsDone == true
            }).ToList();
        }

        if (req is { TotalCount: not null, DoneCount: null })
        {
            entity.DoneCount = 0;
        }

        return Task.CompletedTask;
    }
}
