using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;
public record ActivityCreatedIsOnToDoListEvent(long ActivityId, long TaskUrgencyId) : IEvent;