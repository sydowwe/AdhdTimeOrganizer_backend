namespace Sydowwe.Framework.application.dto.request.user;

public record ResetPasswordRequest
{
    public required long UserId { get; set; }
    public required string Token { get; set; }
    public required string NewPassword { get; set; }
    public required string RecaptchaToken { get; set; }
}