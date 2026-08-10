using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using AdhdTimeOrganizer.infrastructure.extService.googleCalendar;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.googleCalendar;

public record GoogleCalendarAuthUrlResponse(string Url);

public class GetGoogleCalendarAuthUrlEndpoint(IGoogleCalendarService googleCalendarService)
    : EndpointWithoutRequest<GoogleCalendarAuthUrlResponse>
{
    public override void Configure()
    {
        Get("/user/google-calendar/auth-url");
        Summary(s => { s.Summary = "Get Google Calendar OAuth authorization URL"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = googleCalendarService.GenerateState();
        HttpContext.Response.Cookies.Append(GoogleCalendarService.StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/user/google-calendar",
            Expires = DateTimeOffset.UtcNow.AddMinutes(10),
            IsEssential = true
        });

        var url = googleCalendarService.GetAuthUrl(state);
        await Send.OkAsync(new GoogleCalendarAuthUrlResponse(url), ct);
    }
}