using System.Security.Cryptography;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.infrastructure.extService.googleCalendar;

public class GoogleCalendarService : IGoogleCalendarService, ISingletonService
{
    public const string StateCookieName = "google-oauth-state";
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";

    private readonly IConfiguration _configuration;
    private readonly Lazy<GoogleAuthorizationCodeFlow> _flow;

    public GoogleCalendarService(IConfiguration configuration)
    {
        _configuration = configuration;
        // The flow carries no per-user state (only client id/secret/scopes), so it is safe to cache
        // for the lifetime of this singleton instead of building a new one (and its underlying
        // HttpClient) on every call.
        _flow = new Lazy<GoogleAuthorizationCodeFlow>(() => new GoogleAuthorizationCodeFlow(
            new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _configuration["GoogleCalendar:ClientId"]!,
                    ClientSecret = _configuration["GoogleCalendar:ClientSecret"]!
                },
                Scopes = [CalendarScope]
            }));
    }

    public string GenerateState() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public string GetAuthUrl(string state)
    {
        var clientId = Uri.EscapeDataString(_configuration["GoogleCalendar:ClientId"]!);
        var redirectUri = Uri.EscapeDataString(_configuration["GoogleCalendar:RedirectUri"]!);
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
        var redirectUri = _configuration["GoogleCalendar:RedirectUri"]!;
        try
        {
            var tokenResponse = await _flow.Value.ExchangeCodeForTokenAsync("user", code, redirectUri, CancellationToken.None);
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
}
