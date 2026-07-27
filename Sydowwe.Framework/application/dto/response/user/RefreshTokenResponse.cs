namespace Sydowwe.Framework.application.dto.response.user;

/// <summary>Rotated token pair handed to a token-based client. Web clients get theirs as cookies.</summary>
public record RefreshTokenResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
}
