using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record VerifyUserRequest
{
    public string? TwoFactorAuthToken { get; set; }
    public required string Password { get; set; }
}