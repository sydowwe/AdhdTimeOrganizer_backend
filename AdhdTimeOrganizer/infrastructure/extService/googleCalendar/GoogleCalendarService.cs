using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;

namespace AdhdTimeOrganizer.infrastructure.extService.googleCalendar;

public class GoogleCalendarService(IConfiguration configuration) : IGoogleCalendarService, ISingletonService
{
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";

    private GoogleAuthorizationCodeFlow CreateFlow()
    {
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = configuration["GoogleCalendar:ClientId"]!,
                ClientSecret = configuration["GoogleCalendar:ClientSecret"]!
            },
            Scopes = [CalendarScope]
        });
    }

    public string GetAuthUrl()
    {
        var clientId = Uri.EscapeDataString(configuration["GoogleCalendar:ClientId"]!);
        var redirectUri = Uri.EscapeDataString(configuration["GoogleCalendar:RedirectUri"]!);
        var scope = Uri.EscapeDataString(CalendarScope);

        return "https://accounts.google.com/o/oauth2/v2/auth" +
               $"?client_id={clientId}" +
               $"&redirect_uri={redirectUri}" +
               "&response_type=code" +
               $"&scope={scope}" +
               "&access_type=offline" +
               "&prompt=consent";
    }

    public async Task<string?> ExchangeCodeForRefreshToken(string code)
    {
        var redirectUri = configuration["GoogleCalendar:RedirectUri"]!;
        var flow = CreateFlow();
        var tokenResponse = await flow.ExchangeCodeForTokenAsync("user", code, redirectUri, CancellationToken.None);
        return tokenResponse.RefreshToken;
    }

    public CalendarService GetCalendarService(string refreshToken)
    {
        var flow = CreateFlow();
        var credential = new UserCredential(flow, "user", new TokenResponse { RefreshToken = refreshToken });
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ADHD Time Organizer"
        });
    }
}
