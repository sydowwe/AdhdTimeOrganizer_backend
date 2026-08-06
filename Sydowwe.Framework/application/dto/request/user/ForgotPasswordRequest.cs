namespace Sydowwe.Framework.application.dto.request.user;

public record ForgotPasswordRequest
{
    public required string Email { get; init; }
    public required string RecaptchaToken { get; init; }
}