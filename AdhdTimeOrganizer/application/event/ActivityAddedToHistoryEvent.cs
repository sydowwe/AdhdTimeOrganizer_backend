using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.dto;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;

public record ActivityAddedToHistoryEvent : IEvent
{
    public required long UserId { get; init; }
    public required long ActivityId { get; init; }
    public required DateTime StartTimestamp { get; init; }
    public required MyIntTime Length { get; init; }
}