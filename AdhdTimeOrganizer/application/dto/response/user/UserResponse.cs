using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.user;

public record UserResponse : IdResponse
{
    public required string Email { get; init; }
    public bool TwoFactorEnabled { get; init; }
}