using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;

public record TodoListItemIsDoneChangedEvent(long TodoListItemId, bool NewIsDone) : IEvent;
