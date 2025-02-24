using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.domain.@event;
using MediatR;

namespace AdhdTimeOrganizer.Command.application.eventHandler;

public class ActivityAddedToRoutineToDoListEventHandler(IActivityService activityService) : INotificationHandler<ActivityAddedToRoutineToDoListEvent>
{
    public async Task Handle(ActivityAddedToRoutineToDoListEvent notification, CancellationToken cancellationToken)
    {
        var activityResult = await activityService.GetByIdAsync(notification.ActivityId);
        var activity = activityResult.Data;
        activity.IsOnRoutineToDoList = true;
        await activityService.UpdateAsync(activity);
    }
}