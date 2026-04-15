using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityCreatedIsOnTodoListEventHandler(IServiceScopeFactory scopeFactory, ILogger<ActivityCreatedIsOnTodoListEventHandler> logger) : IEventHandler<ActivityCreatedIsOnTodoListEvent>
{
    public async Task HandleAsync(ActivityCreatedIsOnTodoListEvent eventModel, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var toDoList = new TodoListItem { UserId = eventModel.UserId, ActivityId = eventModel.ActivityId, TaskPriorityId = eventModel.TaskPriorityId };
        var result = await dbContext.AddEntityAsync(toDoList, ct);
        if (result.Failed)
        {
            logger.LogError(result.ErrorMessage);
        }
    }
}
