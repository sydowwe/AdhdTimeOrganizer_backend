using Microsoft.AspNetCore.Http;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.domain.extServiceContract.user.auth;

// Non-generic surface: used by RefreshTokenEndpoint and cookie helpers (no user object needed).
public interface IJwtService
{
    Task<RefreshTokenResult> RefreshTokensAsync(string refreshToken, HttpContext httpContext);
    void SetTokenCookies(HttpContext httpContext, string accessToken, string refreshToken, bool stayLoggedIn);

    /// <summary>
    /// Mints the short-lived partial-auth ("2FA pending") token after a successful password step and
    /// returns it as a string, leaving the transport to the caller. Carries only the user id and a
    /// <c>purpose</c> marker — no role claims — so it cannot authenticate against application
    /// endpoints. <paramref name="requiresSetup"/> is persisted in the token so the validate step
    /// knows whether the user still has to provision an authenticator.
    /// <para>Cookie-based (browser) callers should use <see cref="IssueTwoFactorPendingCookie"/>.
    /// Token-based clients — browser extension, desktop app, mobile — hand this string back to the
    /// client and pass it to <see cref="ReadTwoFactorPendingToken"/> on the validate call, so the 2FA
    /// step-up is not tied to cookie auth.</para>
    /// </summary>
    string IssueTwoFactorPendingToken(long userId, bool requiresSetup);

    /// <summary>Validates a raw partial-auth token and returns its contents, or <c>null</c> if invalid/expired.</summary>
    TwoFactorPendingInfo? ReadTwoFactorPendingToken(string? token);

    /// <summary>
    /// Cookie transport for <see cref="IssueTwoFactorPendingToken"/> — writes the token to the
    /// dedicated partial-auth cookie. Browser clients only.
    /// </summary>
    void IssueTwoFactorPendingCookie(HttpContext httpContext, long userId, bool requiresSetup);

    /// <summary>Validates the partial-auth cookie and returns its contents, or <c>null</c> if absent/invalid/expired.</summary>
    TwoFactorPendingInfo? ReadTwoFactorPendingCookie(HttpContext httpContext);

    /// <summary>Clears the partial-auth cookie (call once 2FA is satisfied and the real session is issued).</summary>
    void ClearTwoFactorPendingCookie(HttpContext httpContext);
}

// Generic surface: used by login endpoints and any flow that hands a typed user to JWT creation.
public interface IJwtService<TUser> : IJwtService where TUser : BaseUser
{
    Task GenerateJwtAndSetAuthCookie(bool stayLoggedIn, AuthMethodEnum authMethod, TUser user, HttpContext httpContext);

    /// <summary>Issues a token pair for a browser-extension client — no cookies, tokens are returned directly.</summary>
    Task<(string AccessToken, string RefreshToken)> GenerateTokensForExtensionAsync(AuthMethodEnum authMethod, TUser user);
}