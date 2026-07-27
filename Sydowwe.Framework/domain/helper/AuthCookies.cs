using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Sydowwe.Framework.domain.helper;

/// <summary>
/// Single source of truth for the auth / refresh cookie names and attributes. Centralizes the
/// SameSite / Secure / Path policy so the cookies are written — and deleted — identically
/// everywhere (set on login/refresh, cleared on logout).
///
/// <para><b>SameSite=Strict</b>: the SPA and API are deployed on the same site — same registrable
/// domain (subdomains such as <c>app.</c>/<c>api.</c> are same-site) over the same scheme, so the
/// browser sends the cookie on every same-site request, including the SignalR WebSocket handshake to
/// <c>/hubs</c> (the port difference is invisible to SameSite — that's an origin distinction, not a
/// site one). This is how the hub authenticates under httpOnly-cookie auth (the JWT is never exposed
/// to JS). Strict gives the strongest CSRF protection: the cookie is withheld on <i>every</i>
/// cross-site request, including top-level navigations. The one cost vs <c>Lax</c>: a user following
/// an external deep-link (email, another site) into the app lands unauthenticated on that first
/// top-level GET and must navigate once more inside the app — acceptable for a login-walled portal.
/// If the SPA is ever served from a different registrable domain than the API (genuinely cross-site),
/// neither Strict nor Lax will carry the cookie — switch to <c>SameSiteMode.None</c> (which then
/// requires <c>Secure</c> + CORS <c>AllowCredentials</c>). Note: a host's
/// <c>CookiePolicyOptions.MinimumSameSitePolicy</c> must not be stricter than this value, or it
/// overrides it upward.</para>
/// </summary>
public static class AuthCookies
{
    public const string AuthTokenName = "auth-token";
    public const string RefreshTokenName = "refresh-token";

    /// <summary>
    /// Short-lived cookie holding the partial-auth ("2FA pending") token between the password step
    /// and the 2FA setup/validation step. Deliberately a SEPARATE cookie from <see cref="AuthTokenName"/>
    /// so the JwtBearer handler (which only reads <c>auth-token</c>) never sees it — a partial-auth
    /// token can therefore never authenticate against application endpoints, only the 2FA endpoints
    /// that read this cookie explicitly.
    /// </summary>
    public const string TwoFactorTokenName = "two-factor-token";

    /// <summary>
    /// Identifies the current session for the session-listing / session-revocation endpoints. Holds a
    /// SHA-256 hash of the refresh token, never the token itself, so a leak of this cookie cannot be
    /// replayed as a credential.
    /// </summary>
    public const string SessionHashName = "session-hash";

    /// <summary>
    /// Refresh cookie is scoped away from every request, but the scope must still cover BOTH
    /// <c>/api/auth/refresh</c> and <c>/api/auth/logout</c>: logout reads this cookie to revoke the
    /// token server-side, and a browser only sends a cookie to paths under its <c>Path</c>. Narrowing
    /// this to <c>/api/auth/refresh</c> silently breaks logout revocation — the cookie is not sent, the
    /// handler sees no token, and the refresh token stays valid in the database until it expires.
    /// </summary>
    public const string RefreshTokenPath = "/api/auth";

    /// <summary>Session-hash is read by the session-management endpoints under <c>/api/user</c>.</summary>
    public const string SessionHashPath = "/api";

    /// <summary>Options for the access-token cookie (site-wide path).</summary>
    public static CookieOptions AuthToken(DateTime? expires) => Build("/", expires);

    /// <summary>Options for the refresh-token cookie (restricted path).</summary>
    public static CookieOptions RefreshToken(DateTime? expires) => Build(RefreshTokenPath, expires);

    /// <summary>Options for the session-hash cookie.</summary>
    public static CookieOptions SessionHash(DateTime? expires) => Build(SessionHashPath, expires);

    /// <summary>
    /// Derives the session-hash cookie value from a refresh token. Kept here so the code that writes
    /// the cookie and the code that matches it against stored sessions cannot drift apart.
    /// </summary>
    public static string DeriveSessionHash(string refreshToken) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    /// <summary>
    /// Clears every cookie that makes up a session — access token, refresh token and session hash.
    /// Use this rather than deleting cookies individually: forgetting one leaves a logged-out browser
    /// holding part of a session, and hand-written <see cref="CookieOptions"/> that don't match the
    /// originals are silently ignored by the browser.
    /// </summary>
    public static void ClearSessionCookies(this HttpResponse response)
    {
        response.Cookies.Delete(AuthTokenName, AuthToken(null));
        response.Cookies.Delete(RefreshTokenName, RefreshToken(null));
        response.Cookies.Delete(SessionHashName, SessionHash(null));
    }

    /// <summary>Options for the partial-auth 2FA cookie (site-wide path so both setup + validate see it).</summary>
    public static CookieOptions TwoFactorToken(DateTime? expires) => Build("/", expires);

    private static CookieOptions Build(string path, DateTime? expires) =>
        new()
        {
            HttpOnly = true,
            Secure = true, // HTTPS-only
            SameSite = SameSiteMode.Strict,
            Path = path,
            Expires = expires,
            IsEssential = true
        };

    /// <summary>
    /// JwtBearer events that source the token from the httpOnly <see cref="AuthTokenName"/> cookie.
    /// The browser attaches that cookie to API calls AND to the SignalR WebSocket handshake
    /// (negotiate + upgrade) on <c>/hubs</c>, so the hub authenticates with the same cookie — no
    /// token is ever exposed to JS. SPA and API are same-site, so <c>SameSite=Strict</c> carries it on
    /// the handshake. If the cookie is absent (non-browser client), the token is left unset and
    /// JwtBearer falls back to the <c>Authorization: Bearer</c> header.
    /// <para>Shared by every host's <c>AddIdentityServices</c> so the cookie name and the code that
    /// reads it stay in one place.</para>
    /// </summary>
    public static JwtBearerEvents BearerFromCookie()
    {
        return new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AuthTokenName, out var cookieToken) &&
                    !string.IsNullOrEmpty(cookieToken))
                    context.Token = cookieToken;

                return Task.CompletedTask;
            }
        };
    }
}