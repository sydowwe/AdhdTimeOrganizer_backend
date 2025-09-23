using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.todoList.command;

public class TodoListCreateEndpoint(AppCommandDbContext dbContext, TodoListMapper mapper, IOptions<TodoListSettings> settings)
    : BaseCreateEndpoint<TodoList, CreateTodoListRequest, TodoListMapper>(dbContext, mapper)
{
    private readonly AppCommandDbContext _dbContext = dbContext;
    private readonly TodoListSettings _settings = settings.Value;


    protected override async Task AfterMapping(TodoList entity, CreateTodoListRequest req, CancellationToken ct = default)
    {
        entity.DisplayOrder = await _dbContext.TodoLists.GetNextDisplayOrder(_settings, User.GetId(), entity.TaskUrgencyId, ct);
        if (req.TotalCount.HasValue)
        {
            entity.DoneCount = 0;
        }
    }
}
