using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;

public record RoutineTodoListIsDoneChangedEvent(long ActivityId, long UserId, bool NewIsDone) : IEvent;
