using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;
public record UserRegisteredEvent(long UserId) : IEvent;