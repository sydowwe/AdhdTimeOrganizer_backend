using MediatR;

namespace AdhdTimeOrganizer.Command.domain.@event;
public record UserRegisteredEvent(long UserId) : INotification;