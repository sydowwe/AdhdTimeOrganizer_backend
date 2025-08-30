using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityCreatedIsOnToDoListEventHandler(IToDoListService toDoListService) : IEventHandler<ActivityCreatedIsOnToDoListEvent>
{
    public async Task Handle(ActivityCreatedIsOnToDoListEvent notification, CancellationToken cancellationToken)
    {
        var toDoList = new ToDoList { ActivityId = notification.ActivityId, TaskUrgencyId = notification.TaskUrgencyId };
        await toDoListService.InsertAsync(toDoList);
    }
}