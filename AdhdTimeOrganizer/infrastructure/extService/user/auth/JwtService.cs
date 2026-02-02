using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

public class JwtService(IEcdsaKeyProvider ecdsaKeyProvider, IRefreshTokenService refreshTokenService, UserManager<User> userManager) : IJwtService, IScopedService
{
    private readonly DateTime _accessTokenExpiry = DateTime.UtcNow.AddMinutes(15);

    public async Task<(string AccessToken, string RefreshToken, bool IsStayLoggedIn)> RefreshTokensAsync(
        string refreshToken, bool isExtensionClient, HttpContext httpContext)
    {
        var (isValid, authMethod, isStayLoggedIn, user, errorMessage) = await refreshTokenService.ValidateRefreshTokenAsync(refreshToken);

        if (!isValid || user == null)
            throw new UnauthorizedAccessException(errorMessage ?? "Invalid refresh token");

        // Revoke old token
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        await refreshTokenService.RevokeRefreshTokenAsync(refreshToken, ipAddress);

        var accessToken = await CreateEcdsaJwtToken(user, authMethod, ClientTypeEnum.Web);
        var newRefreshToken = await refreshTokenService.GenerateRefreshTokenAsync(
            user.Id, isExtensionClient, authMethod, isStayLoggedIn, ipAddress);

        return (accessToken, newRefreshToken, isStayLoggedIn);
    }

    public async Task GenerateJwtAndSetAuthCookie(bool stayLoggedIn, AuthMethodEnum authMethod, User user, HttpContext httpContext)
    {
        var (accessToken, refreshToken) = await GenerateTokensForWebAsync(
            stayLoggedIn, authMethod, user, httpContext);

        SetTokenCookies(httpContext, accessToken, refreshToken, stayLoggedIn);
    }

    public async Task<(string AccessToken, string RefreshToken)> GenerateTokensForExtensionAsync(
        AuthMethodEnum authMethod, User user)
    {
        var accessToken = await CreateEcdsaJwtToken(user, authMethod, ClientTypeEnum.Extension);

        // Extension tokens always 30 days
        var refreshToken = await refreshTokenService.GenerateRefreshTokenAsync(
            user.Id, isExtensionClient: true, authMethod);

        return (accessToken, refreshToken);
    }

    private async Task<(string AccessToken, string RefreshToken)> GenerateTokensForWebAsync(
        bool stayLoggedIn, AuthMethodEnum authMethod, User user, HttpContext httpContext)
    {
        var accessToken = await CreateEcdsaJwtToken(user, authMethod, ClientTypeEnum.Web);

        // Long-lived refresh token
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var refreshToken = await refreshTokenService.GenerateRefreshTokenAsync(
            user.Id, isExtensionClient: false, authMethod, stayLoggedIn, ipAddress);

        return (accessToken, refreshToken);
    }

    private async Task<string> CreateEcdsaJwtToken(User user, AuthMethodEnum authMethod, ClientTypeEnum clientType)
    {
        var claims = await CreateUserClaims(user, userManager, authMethod, clientType);
        var signingCredentials = ecdsaKeyProvider.GetSigningCredentials();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = _accessTokenExpiry,
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

        // Add ActivityTracking role claim for extension clients (not persisted to database)
        if (clientType == ClientTypeEnum.Extension)
        {
            claims.Add(new Claim(ClaimTypes.Role, "ActivityTracking"));
        }

        return claims;
    }

    public void SetTokenCookies(HttpContext httpContext, string accessToken, string refreshToken, bool stayLoggedIn)
    {
        // Access token cookie
        httpContext.Response.Cookies.Append("auth-token", accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = _accessTokenExpiry,
            Path = "/",
            IsEssential = true
        });

        // Refresh token cookie (restricted path)
        httpContext.Response.Cookies.Append("refresh-token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = stayLoggedIn ? DateTime.UtcNow.AddDays(30) : null,
            Path = "/api/user/refresh",
            IsEssential = true
        });
    }
}