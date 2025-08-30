using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityAddedToTodoListEventHandler(AppCommandDbContext dbContext, ILogger<ActivityAddedToTodoListEventHandler> logger) : IEventHandler<ActivityAddedToTodoListEvent>
{
    public async Task HandleAsync(ActivityAddedToTodoListEvent eventModel, CancellationToken ct)
    {
        var activity = await dbContext.Activities.FindAsync([eventModel.ActivityId], ct);
        if (activity == null)
        {
            logger.LogError("Activity with id {EventModelActivityId} not found", eventModel.ActivityId);
            return;
        }
        activity.IsOnTodoList = true;
        var res = await dbContext.UpdateEntityAsync(activity, ct);
        if (res.Failed)
        {
            logger.LogError(res.ErrorMessage);
        }
    }
}