using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.user;

public record EmailResponse : IMyResponse
{
    public required string Email { get; init; }
}