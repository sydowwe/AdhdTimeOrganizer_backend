using FastEndpoints;

namespace AdhdTimeOrganizer.Core.application.@event;

public record PlannerTaskIsDoneChangedEvent(long ActivityId, long UserId, bool NewIsDone, long? TodoListItemId) : IEvent;