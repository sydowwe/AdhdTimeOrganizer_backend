using MediatR;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.domain.@event;

namespace AdhdTimeOrganizer.Command.application.eventHandler;

public class UserRegisteredEventHandler(
    IRoleService roleService,
    ITaskUrgencyService taskUrgencyService,
    IRoutineTimePeriodService routineTimePeriodService)
    : INotificationHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        await roleService.CreateDefaultItems(notification.UserId);
        await taskUrgencyService.CreateDefaultItems(notification.UserId);
        await routineTimePeriodService.CreateDefaultItems(notification.UserId);
    }
}