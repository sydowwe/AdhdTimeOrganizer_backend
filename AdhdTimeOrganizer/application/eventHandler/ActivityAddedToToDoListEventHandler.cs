using AdhdTimeOrganizer.application.@event;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityAddedToToDoListEventHandler(IActivityService activityService) : IEventHandler<ActivityAddedToToDoListEvent>
{
    public async Task Handle(ActivityAddedToToDoListEvent notification, CancellationToken cancellationToken)
    {
        var activityResult = await activityService.GetByIdAsync(notification.ActivityId);
        var activity = activityResult.Data;
        activity.IsOnToDoList = true;
        await activityService.UpdateAsync(activity);
    }
}