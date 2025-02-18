using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public record EmailResponse : IMyResponse
{
    public required string Email { get; init; }
}