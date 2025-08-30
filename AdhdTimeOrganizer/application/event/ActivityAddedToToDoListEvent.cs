using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;

public record ActivityAddedToToDoListEvent(long ActivityId) : IEvent;