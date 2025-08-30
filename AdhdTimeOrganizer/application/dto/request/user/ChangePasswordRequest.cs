using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ChangePasswordRequest : IMyRequest
{
    public string? TwoFactorAuthToken { get; set; }
    public string CurrentPassword { get; set; }
    public string NewPassword { get; set; }
}