using AdhdTimeOrganizer.application.@event;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class UserRegisteredEventHandler(
    IRoleService roleService,
    ITaskUrgencyService taskUrgencyService,
    IRoutineTimePeriodService routineTimePeriodService)
    : IEventHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        await roleService.CreateDefaultItems(notification.UserId);
        await taskUrgencyService.CreateDefaultItems(notification.UserId);
        await routineTimePeriodService.CreateDefaultItems(notification.UserId);
    }
}