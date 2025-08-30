using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;

public record ActivityAddedToRoutineToDoListEvent(long ActivityId) : IEvent;