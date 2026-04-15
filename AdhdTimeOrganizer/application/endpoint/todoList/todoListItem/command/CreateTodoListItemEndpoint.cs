using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.Extensions.Options;
using TodoListItemMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListItemMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class CreateTodoListItemEndpoint(AppDbContext dbContext, TodoListItemMapper mapper, IOptions<TodoListSettings> settings)
    : BaseCreateEndpoint<TodoListItem, CreateTodoListItemRequest, TodoListItemMapper>(dbContext, mapper)
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly TodoListSettings _settings = settings.Value;

    protected override async Task AfterMapping(TodoListItem entity, CreateTodoListItemRequest req, CancellationToken ct = default)
    {
        entity.DisplayOrder = await _dbContext.TodoListItems.GetNextDisplayOrder(_settings, User.GetId(), entity.TaskPriorityId, ct);
        entity.TodoListId = req.TodoListId;

        if (req.Steps is { Count: > 0 })
        {
            entity.Steps = req.Steps.Select(s => new TodoListStep { Name = s.Name, Order = s.Order, Note = s.Note }).ToList();
            entity.DoneCount = 0;
        }
        else if (req.TotalCount.HasValue)
        {
            entity.DoneCount = 0;
        }
    }
}
