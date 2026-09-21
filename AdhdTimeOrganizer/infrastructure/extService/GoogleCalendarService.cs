using System.Security.Cryptography;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.infrastructure.extService.googleCalendar;

/// <summary>
/// Google OAuth for the calendar scope. Configuration (B14):
/// <list type="bullet">
/// <item><b>Client id / secret</b> — <c>GoogleCalendar:ClientId</c> / <c>GoogleCalendar:ClientSecret</c>
/// when set, otherwise the sign-in client (<c>OAUTH2_GOOGLE_CLIENT_ID</c> / <c>_SECRET</c>). One Google
/// Cloud OAuth client normally serves both sign-in and the calendar consent; the section exists for a
/// deployment that wants them apart. Neither present → <see cref="IsConfigured"/> is <c>false</c> and
/// the endpoints say so instead of building a URL Google rejects with "Missing required parameter:
/// client_id".</item>
/// <item><b>Redirect URI</b> — always <c>PAGE_URL</c> + <see cref="CallbackPath"/>, the same origin the
/// confirm-email links use, so it cannot drift from where the SPA actually listens. It is not a config
/// key on purpose: the old literal pointed at a port nothing served. It still has to be registered in
/// the Google console for every environment's <c>PAGE_URL</c>.</item>
/// </list>
/// </summary>
public class GoogleCalendarService : IGoogleCalendarService, ISingletonService
{
    public const string StateCookieName = "google-oauth-state";

    /// <summary>The SPA route that receives Google's <c>?code=&amp;state=</c> (<c>googleCalendar.routes.ts</c>).</summary>
    public const string CallbackPath = "/google-calendar/callback";

    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";

    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly Lazy<GoogleAuthorizationCodeFlow> _flow;

    public GoogleCalendarService(IConfiguration configuration)
    {
        _clientId = FirstNonEmpty(configuration["GoogleCalendar:ClientId"], Environment.GetEnvironmentVariable("OAUTH2_GOOGLE_CLIENT_ID"));
        _clientSecret = FirstNonEmpty(configuration["GoogleCalendar:ClientSecret"], Environment.GetEnvironmentVariable("OAUTH2_GOOGLE_CLIENT_SECRET"));

        // The flow carries no per-user state (only client id/secret/scopes), so it is safe to cache
        // for the lifetime of this singleton instead of building a new one (and its underlying
        // HttpClient) on every call.
        _flow = new Lazy<GoogleAuthorizationCodeFlow>(() => new GoogleAuthorizationCodeFlow(
            new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = RequireConfigured(_clientId),
                    ClientSecret = RequireConfigured(_clientSecret)
                },
                Scopes = [CalendarScope]
            }));
    }

    public bool IsConfigured => _clientId is not null && _clientSecret is not null;

    /// <summary>Where Google sends the user back: the SPA origin plus <see cref="CallbackPath"/>.</summary>
    public static string RedirectUri => Helper.GetEnvVar("PAGE_URL").TrimEnd('/') + CallbackPath;

    public string GenerateState() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public string GetAuthUrl(string state)
    {
        var clientId = Uri.EscapeDataString(RequireConfigured(_clientId));
        var redirectUri = Uri.EscapeDataString(RedirectUri);
        var scope = Uri.EscapeDataString(CalendarScope);
        var encodedState = Uri.EscapeDataString(state);

        return "https://accounts.google.com/o/oauth2/v2/auth" +
               $"?client_id={clientId}" +
               $"&redirect_uri={redirectUri}" +
               "&response_type=code" +
               $"&scope={scope}" +
               "&access_type=offline" +
               "&prompt=consent" +
               $"&state={encodedState}";
    }

    public async Task<string?> ExchangeCodeForRefreshToken(string code)
    {
        try
        {
            var tokenResponse = await _flow.Value.ExchangeCodeForTokenAsync("user", code, RedirectUri, CancellationToken.None);
            return tokenResponse.RefreshToken;
        }
        catch (TokenResponseException)
        {
            // Invalid/expired authorization code, revoked consent, or a client-config mismatch — the
            // caller already treats a null refresh token as "failed to obtain token" (400), so no
            // Google error payload is exposed to the client and none of the token response is logged.
            return null;
        }
    }

    public CalendarService GetCalendarService(string refreshToken)
    {
        var credential = new UserCredential(_flow.Value, "user", new TokenResponse { RefreshToken = refreshToken });
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ADHD Time Organizer"
        });
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback)
        => !string.IsNullOrWhiteSpace(preferred) ? preferred
            : !string.IsNullOrWhiteSpace(fallback) ? fallback
            : null;

    // Every endpoint checks IsConfigured before calling in, so reaching this is a programming error,
    // not a runtime condition to report to the user.
    private static string RequireConfigured(string? value)
        => value ?? throw new InvalidOperationException(
            "Google Calendar is not configured: set GoogleCalendar:ClientId/ClientSecret or OAUTH2_GOOGLE_CLIENT_ID/_SECRET. Check IGoogleCalendarService.IsConfigured first.");
}
