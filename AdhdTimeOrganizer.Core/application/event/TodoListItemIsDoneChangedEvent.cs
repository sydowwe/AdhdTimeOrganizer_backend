using FastEndpoints;

namespace AdhdTimeOrganizer.Core.application.@event;

public record TodoListItemIsDoneChangedEvent(long TodoListItemId, bool NewIsDone) : IEvent;