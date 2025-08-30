using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;
public record ActivityCreatedIsOnTodoListEvent(long UserId, long ActivityId, long TaskUrgencyId) : IEvent;