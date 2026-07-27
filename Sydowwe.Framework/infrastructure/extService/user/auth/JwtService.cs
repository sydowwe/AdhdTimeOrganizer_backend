using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.extService.user.auth;

public class JwtService<TUser>(
    IEcdsaKeyProvider ecdsaKeyProvider,
    IRefreshTokenService refreshTokenService,
    UserManager<TUser> userManager,
    IEnumerable<IAdditionalUserClaimsProvider<TUser>> additionalClaimsProviders)
    : IJwtService<TUser>
    where TUser : BaseUser
{
    private static DateTime AccessTokenExpiry => DateTime.UtcNow.AddMinutes(15);
    private static DateTime TwoFactorPendingExpiry => DateTime.UtcNow.AddMinutes(10);

    private const string TwoFactorPurposeClaim = "purpose";
    private const string TwoFactorPurposeValue = "2fa-pending";
    private const string TwoFactorSetupClaim = "tfa_setup";

    public async Task<RefreshTokenResult> RefreshTokensAsync(string refreshToken, HttpContext httpContext)
    {
        var (isValid, authMethod, isStayLoggedIn, isExtensionClient, userId, errorMessage) = await refreshTokenService.ValidateRefreshTokenAsync(refreshToken);

        if (!isValid || userId == null)
            return RefreshTokenResult.Fail(errorMessage ?? "Invalid refresh token");

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        var newRefreshToken = await refreshTokenService.RotateRefreshTokenAsync(
            refreshToken, userId.Value, authMethod, isStayLoggedIn, ipAddress, isExtensionClient, userAgent);

        if (newRefreshToken == null)
            return RefreshTokenResult.Fail("Refresh token has already been used");

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user == null)
            return RefreshTokenResult.Fail("User not found");

        var clientType = isExtensionClient ? ClientTypeEnum.Extension : ClientTypeEnum.Web;
        var accessToken = await CreateEcdsaJwtToken(user, authMethod, clientType);

        return RefreshTokenResult.Ok(accessToken, newRefreshToken, isStayLoggedIn);
    }

    public async Task GenerateJwtAndSetAuthCookie(bool stayLoggedIn, AuthMethodEnum authMethod, TUser user, HttpContext httpContext)
    {
        var accessToken = await CreateEcdsaJwtToken(user, authMethod, ClientTypeEnum.Web);

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var refreshToken = await refreshTokenService.GenerateRefreshTokenAsync(
            user.Id, authMethod, stayLoggedIn, ipAddress, false, userAgent);

        SetTokenCookies(httpContext, accessToken, refreshToken, stayLoggedIn);
    }

    public async Task<(string AccessToken, string RefreshToken)> GenerateTokensForExtensionAsync(AuthMethodEnum authMethod, TUser user)
    {
        var accessToken = await CreateEcdsaJwtToken(user, authMethod, ClientTypeEnum.Extension);

        var refreshToken = await refreshTokenService.GenerateRefreshTokenAsync(
            user.Id, authMethod, true, "unknown", true);

        return (accessToken, refreshToken);
    }

    private async Task<string> CreateEcdsaJwtToken(TUser user, AuthMethodEnum authMethod, ClientTypeEnum clientType)
    {
        var claims = await CreateUserClaims(user, authMethod, clientType);
        return WriteEcdsaJwt(claims, AccessTokenExpiry);
    }

    private string WriteEcdsaJwt(IEnumerable<Claim> claims, DateTime expires)
    {
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = Helper.GetEnvVar("JWT_ISSUER"),
            Audience = Helper.GetEnvVar("JWT_AUDIENCE"),
            SigningCredentials = ecdsaKeyProvider.GetSigningCredentials()
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string IssueTwoFactorPendingToken(long userId, bool requiresSetup)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(TwoFactorPurposeClaim, TwoFactorPurposeValue),
            new(TwoFactorSetupClaim, requiresSetup ? "true" : "false")
        };

        return WriteEcdsaJwt(claims, TwoFactorPendingExpiry);
    }

    public void IssueTwoFactorPendingCookie(HttpContext httpContext, long userId, bool requiresSetup)
    {
        httpContext.Response.Cookies.Append(
            AuthCookies.TwoFactorTokenName, IssueTwoFactorPendingToken(userId, requiresSetup),
            AuthCookies.TwoFactorToken(TwoFactorPendingExpiry));
    }

    public TwoFactorPendingInfo? ReadTwoFactorPendingCookie(HttpContext httpContext) =>
        httpContext.Request.Cookies.TryGetValue(AuthCookies.TwoFactorTokenName, out var token)
            ? ReadTwoFactorPendingToken(token)
            : null;

    public TwoFactorPendingInfo? ReadTwoFactorPendingToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = Helper.GetEnvVar("JWT_ISSUER"),
            ValidAudience = Helper.GetEnvVar("JWT_AUDIENCE"),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = ecdsaKeyProvider.GetSigningKey(),
            ValidAlgorithms = [ecdsaKeyProvider.SecurityAlgorithm],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            // Keep the raw JWT claim names (e.g. "sub") instead of remapping them to the
            // long ClaimTypes URIs, so the lookups below find the claims we wrote.
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, validationParameters, out _);

            if (principal.FindFirstValue(TwoFactorPurposeClaim) != TwoFactorPurposeValue)
                return null;

            var userIdString = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (!long.TryParse(userIdString, out var userId))
                return null;

            var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (string.IsNullOrEmpty(tokenId))
                return null;

            var requiresSetup = principal.FindFirstValue(TwoFactorSetupClaim) == "true";
            return new TwoFactorPendingInfo(userId, requiresSetup, tokenId);
        }
        catch (Exception)
        {
            // Invalid signature, expired, malformed — treat as no valid partial session.
            return null;
        }
    }

    public void ClearTwoFactorPendingCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Append(
            AuthCookies.TwoFactorTokenName, string.Empty, AuthCookies.TwoFactorToken(DateTime.UtcNow.AddDays(-1)));
    }

    private async Task<List<Claim>> CreateUserClaims(TUser user, AuthMethodEnum authMethod, ClientTypeEnum clientType)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(AuthClaims.AuthMethod, authMethod.ToString()),
            new(AuthClaims.ClientType, clientType.ToString())
        };

        var userRoles = await userManager.GetRolesAsync(user);
        claims.AddRange(userRoles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Deployment-specific claims (e.g. employee_id, or an extension-only role grant) contributed
        // via the extension seam. Runs on both login and refresh since both mint through here.
        foreach (var provider in additionalClaimsProviders)
            claims.AddRange(await provider.GetClaimsAsync(user, clientType));

        return claims;
    }

    public void SetTokenCookies(HttpContext httpContext, string accessToken, string refreshToken, bool stayLoggedIn)
    {
        // All three go through AuthCookies so the attributes used to SET a cookie and the attributes
        // used to DELETE it (on logout / password change) cannot drift apart — a delete whose path or
        // SameSite doesn't match the original is silently ignored by the browser.
        var sessionExpiry = stayLoggedIn ? DateTime.UtcNow.AddDays(30) : (DateTime?)null;

        httpContext.Response.Cookies.Append(
            AuthCookies.AuthTokenName, accessToken, AuthCookies.AuthToken(AccessTokenExpiry));

        httpContext.Response.Cookies.Append(
            AuthCookies.RefreshTokenName, refreshToken, AuthCookies.RefreshToken(sessionExpiry));

        // Identifies the current session to the session-listing / revocation endpoints (hash only,
        // never the raw token). Rewritten on every refresh because rotation changes the token, and a
        // stale hash would make the caller's own session stop matching itself.
        httpContext.Response.Cookies.Append(
            AuthCookies.SessionHashName, AuthCookies.DeriveSessionHash(refreshToken),
            AuthCookies.SessionHash(sessionExpiry));
    }
}