using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface IJwtService
{
    Task<RefreshTokenResult> RefreshTokensAsync(string refreshToken, HttpContext httpContext);

    Task GenerateJwtAndSetAuthCookie(bool stayLoggedIn, AuthMethodEnum authMethod, User user, HttpContext httpContext);

    Task<(string AccessToken, string RefreshToken)> GenerateTokensForExtensionAsync(AuthMethodEnum authMethod, User user);

    void SetTokenCookies(HttpContext httpContext, string accessToken, string refreshToken, bool stayLoggedIn);
}