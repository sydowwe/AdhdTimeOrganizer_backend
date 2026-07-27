namespace Sydowwe.Framework.application.dto.request.user;

public record VerifyUserRequest
{
    public string? TwoFactorAuthToken { get; set; }
    public required string Password { get; set; }
}