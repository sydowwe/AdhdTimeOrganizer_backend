using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;

public record PlannerTaskIsDoneChangedEvent(long ActivityId, long UserId, bool NewIsDone, long? TodoListItemId) : IEvent;
