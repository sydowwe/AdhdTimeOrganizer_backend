namespace Sydowwe.Framework.application.dto.request.user;

/// <summary>
/// Body-carried refresh token, used by the extension refresh and logout routes. Web clients send
/// nothing — their refresh token rides in the cookie, so the web bases are
/// <c>EndpointWithoutRequest</c>.
/// </summary>
public record RefreshTokenRequest
{
    public string? RefreshToken { get; init; }
}
