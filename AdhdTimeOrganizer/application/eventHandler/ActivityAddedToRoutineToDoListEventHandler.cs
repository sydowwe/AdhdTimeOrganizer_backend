using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityAddedToRoutineTodoListEventHandler(AppCommandDbContext dbContext, ILogger<ActivityAddedToRoutineTodoListEventHandler> logger) : IEventHandler<ActivityAddedToRoutineTodoListEvent>
{
    public async Task HandleAsync(ActivityAddedToRoutineTodoListEvent eventModel, CancellationToken ct)
    {
        var activity = await dbContext.Activities.FindAsync([eventModel.ActivityId], ct);
        if (activity == null)
        {
            logger.LogError("Activity with id {EventModelActivityId} not found", eventModel.ActivityId);
            return;
        }
        activity.IsOnRoutineTodoList = true;
        var res = await dbContext.UpdateEntityAsync(activity, ct);
        if (res.Failed)
        {
            logger.LogError(res.ErrorMessage);
        }
    }
}