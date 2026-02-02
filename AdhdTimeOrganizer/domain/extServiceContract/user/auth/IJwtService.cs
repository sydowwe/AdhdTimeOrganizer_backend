using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface IJwtService
{
    Task<(string AccessToken, string RefreshToken, bool IsStayLoggedIn)> RefreshTokensAsync(string refreshToken, bool isExtensionClient, HttpContext httpContext);

    Task GenerateJwtAndSetAuthCookie(bool stayLoggedIn, AuthMethodEnum authMethod, User user, HttpContext httpContext);

    Task<(string AccessToken, string RefreshToken)> GenerateTokensForExtensionAsync(AuthMethodEnum authMethod, User user);

    void SetTokenCookies(HttpContext httpContext, string accessToken, string refreshToken, bool stayLoggedIn);
}