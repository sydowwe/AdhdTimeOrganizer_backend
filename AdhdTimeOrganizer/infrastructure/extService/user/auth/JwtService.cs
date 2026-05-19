using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

public class JwtService(IEcdsaKeyProvider ecdsaKeyProvider, IRefreshTokenService refreshTokenService, UserManager<User> userManager) : IJwtService, IScopedService
{
    private static DateTime AccessTokenExpiry => DateTime.UtcNow.AddMinutes(15);

    public async Task<RefreshTokenResult> RefreshTokensAsync(string refreshToken, HttpContext httpContext)
    {
        var (isValid, authMethod, isStayLoggedIn, isExtensionClient, user, errorMessage) = await refreshTokenService.ValidateRefreshTokenAsync(refreshToken);

        if (!isValid || user == null)
            return RefreshTokenResult.Fail(errorMessage ?? "Invalid refresh token");

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        await refreshTokenService.RevokeRefreshTokenAsync(refreshToken, ipAddress);

        var clientType = isExtensionClient ? ClientTypeEnum.Extension : ClientTypeEnum.Web;
        var accessToken = await CreateEcdsaJwtToken(user, authMethod, clientType);
        var newRefreshToken = await refreshTokenService.GenerateRefreshTokenAsync(
            user.Id, isExtensionClient, authMethod, isStayLoggedIn, ipAddress, userAgent);

        return RefreshTokenResult.Ok(accessToken, newRefreshToken, isStayLoggedIn);
    }

    public async Task GenerateJwtAndSetAuthCookie(bool stayLoggedIn, AuthMethodEnum authMethod, User user, HttpContext httpContext)
    {
        var (accessToken, refreshToken) = await GenerateTokensForWebAsync(
            stayLoggedIn, authMethod, user, httpContext);

        user.LastLoginAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        SetTokenCookies(httpContext, accessToken, refreshToken, stayLoggedIn);
    }

    public async Task<(string AccessToken, string RefreshToken)> GenerateTokensForExtensionAsync(
        AuthMethodEnum authMethod, User user)
    {
        var accessToken = await CreateEcdsaJwtToken(user, authMethod, ClientTypeEnum.Extension);

        var refreshToken = await refreshTokenService.GenerateRefreshTokenAsync(
            user.Id, isExtensionClient: true, authMethod);

        user.LastLoginAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        return (accessToken, refreshToken);
    }

    private async Task<(string AccessToken, string RefreshToken)> GenerateTokensForWebAsync(
        bool stayLoggedIn, AuthMethodEnum authMethod, User user, HttpContext httpContext)
    {
        var accessToken = await CreateEcdsaJwtToken(user, authMethod, ClientTypeEnum.Web);

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var refreshToken = await refreshTokenService.GenerateRefreshTokenAsync(
            user.Id, isExtensionClient: false, authMethod, stayLoggedIn, ipAddress, userAgent);

        return (accessToken, refreshToken);
    }

    private async Task<string> CreateEcdsaJwtToken(User user, AuthMethodEnum authMethod, ClientTypeEnum clientType)
    {
        var claims = await CreateUserClaims(user, userManager, authMethod, clientType);
        var signingCredentials = ecdsaKeyProvider.GetSigningCredentials();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = AccessTokenExpiry,
            Issuer = Helper.GetEnvVar("JWT_ISSUER"),
            Audience = Helper.GetEnvVar("JWT_AUDIENCE"),
            SigningCredentials = signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static async Task<List<Claim>> CreateUserClaims(User user, UserManager<User> userManager, AuthMethodEnum authMethod, ClientTypeEnum clientType)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("auth_method", authMethod.ToString()),
            new("client_type", clientType.ToString())
        };

        var userRoles = await userManager.GetRolesAsync(user);
        claims.AddRange(userRoles.Select(role => new Claim(ClaimTypes.Role, role)));

        if (clientType == ClientTypeEnum.Extension)
        {
            claims.Add(new Claim(ClaimTypes.Role, "ActivityTracking"));
        }

        return claims;
    }

    public void SetTokenCookies(HttpContext httpContext, string accessToken, string refreshToken, bool stayLoggedIn)
    {
        httpContext.Response.Cookies.Append("auth-token", accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = AccessTokenExpiry,
            Path = "/",
            IsEssential = true
        });

        httpContext.Response.Cookies.Append("refresh-token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = stayLoggedIn ? DateTime.UtcNow.AddDays(30) : null,
            Path = "/api/auth",
            IsEssential = true
        });

        // Identifies the current session for /user/sessions (hash only, not the raw token)
        var sessionHash = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken)));
        httpContext.Response.Cookies.Append("session-hash", sessionHash, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = stayLoggedIn ? DateTime.UtcNow.AddDays(30) : null,
            Path = "/api",
            IsEssential = true
        });
    }
}
