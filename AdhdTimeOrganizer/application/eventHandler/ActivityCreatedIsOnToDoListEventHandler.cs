using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityCreatedIsOnTodoListEventHandler(AppCommandDbContext dbContext, ILogger<ActivityCreatedIsOnTodoListEventHandler> logger) : IEventHandler<ActivityCreatedIsOnTodoListEvent>
{
    public async Task HandleAsync(ActivityCreatedIsOnTodoListEvent eventModel, CancellationToken ct)
    {
        var toDoList = new TodoList { UserId = eventModel.UserId, ActivityId = eventModel.ActivityId, TaskPriorityId = eventModel.TaskPriorityId };
        var result = await dbContext.AddEntityAsync(toDoList, ct);
        if (result.Failed)
        {
            logger.LogError(result.ErrorMessage);
        }
    }
}