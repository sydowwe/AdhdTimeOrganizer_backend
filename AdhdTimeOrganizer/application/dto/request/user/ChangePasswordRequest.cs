using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ChangePasswordRequest : IMyRequest
{
    public string? TwoFactorAuthToken { get; set; }
    public required string CurrentPassword { get; set; }
    public required string NewPassword { get; set; }
}