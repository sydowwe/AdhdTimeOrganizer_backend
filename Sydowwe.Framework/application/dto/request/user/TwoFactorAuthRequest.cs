namespace Sydowwe.Framework.application.dto.request.user;

public record TwoFactorAuthRequest
{
    public required string Token { get; set; }
}