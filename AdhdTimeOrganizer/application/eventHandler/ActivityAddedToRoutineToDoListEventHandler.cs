using AdhdTimeOrganizer.application.@event;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityAddedToRoutineToDoListEventHandler(IActivityService activityService) : IEventHandler<ActivityAddedToRoutineToDoListEvent>
{
    public async Task Handle(ActivityAddedToRoutineToDoListEvent notification, CancellationToken cancellationToken)
    {
        var activityResult = await activityService.GetByIdAsync(notification.ActivityId);
        var activity = activityResult.Data;
        activity.IsOnRoutineToDoList = true;
        await activityService.UpdateAsync(activity);
    }
}