using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.domain.@event;
using MediatR;

namespace AdhdTimeOrganizer.Command.application.eventHandler;

public class ActivityAddedToToDoListEventHandler(IActivityService activityService) : INotificationHandler<ActivityAddedToToDoListEvent>
{
    public async Task Handle(ActivityAddedToToDoListEvent notification, CancellationToken cancellationToken)
    {
        var activityResult = await activityService.GetByIdAsync(notification.ActivityId);
        var activity = activityResult.Data;
        activity.IsOnToDoList = true;
        await activityService.UpdateAsync(activity);
    }
}