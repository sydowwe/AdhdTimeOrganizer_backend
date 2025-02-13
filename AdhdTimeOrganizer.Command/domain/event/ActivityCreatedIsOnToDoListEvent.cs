using MediatR;

namespace AdhdTimeOrganizer.Command.domain.@event;
public record ActivityCreatedIsOnToDoListEvent(long ActivityId, long TaskUrgencyId) : INotification;