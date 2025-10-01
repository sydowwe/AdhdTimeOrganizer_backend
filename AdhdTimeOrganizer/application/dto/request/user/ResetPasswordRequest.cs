using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ResetPasswordRequest : IMyRequest
{
    public required long UserId { get; set; }
    public required string Token { get; set; }
    public required string NewPassword { get; set; }
}