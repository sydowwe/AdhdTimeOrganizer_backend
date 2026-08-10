using FastEndpoints;

namespace AdhdTimeOrganizer.Core.application.@event;

public record ActivityAddedToTodoListEvent(long ActivityId) : IEvent;