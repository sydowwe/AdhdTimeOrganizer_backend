using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.@base;

public record UserIdRequest : IMyRequest
{
    public long UserId { get; init; }
}