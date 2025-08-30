using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;

public record ActivityAddedToRoutineTodoListEvent(long ActivityId) : IEvent;