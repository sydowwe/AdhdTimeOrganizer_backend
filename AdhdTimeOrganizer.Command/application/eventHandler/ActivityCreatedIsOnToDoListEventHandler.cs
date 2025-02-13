using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.domain.@event;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using MediatR;

namespace AdhdTimeOrganizer.Command.application.eventHandler;

public class ActivityCreatedIsOnToDoListEventHandler(IToDoListService toDoListService) : INotificationHandler<ActivityCreatedIsOnToDoListEvent>
{
    public async Task Handle(ActivityCreatedIsOnToDoListEvent notification, CancellationToken cancellationToken)
    {
        var toDoList = new ToDoList { ActivityId = notification.ActivityId, TaskUrgencyId = notification.TaskUrgencyId };
        await toDoListService.InsertAsync(toDoList);
    }
}