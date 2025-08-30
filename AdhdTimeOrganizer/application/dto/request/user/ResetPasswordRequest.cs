using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ResetPasswordRequest : IMyRequest
{
    public long UserId { get; set; }
    public string Token { get; set; }
    public string NewPassword { get; set; }
}