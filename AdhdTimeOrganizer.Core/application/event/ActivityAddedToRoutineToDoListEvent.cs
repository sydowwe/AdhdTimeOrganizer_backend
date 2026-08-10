using FastEndpoints;

namespace AdhdTimeOrganizer.Core.application.@event;

public record ActivityAddedToRoutineTodoListEvent(long ActivityId) : IEvent;