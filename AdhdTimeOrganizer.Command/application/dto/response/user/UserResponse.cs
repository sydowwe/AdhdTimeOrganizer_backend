using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public record UserResponse : IdResponse
{
    public required string Email { get; init; }
    public bool TwoFactorEnabled { get; init; }
}