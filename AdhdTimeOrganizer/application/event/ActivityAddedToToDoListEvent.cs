using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;

public record ActivityAddedToTodoListEvent(long ActivityId) : IEvent;