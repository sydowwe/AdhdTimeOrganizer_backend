using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.googleCalendar;

public record GoogleCalendarAuthUrlResponse(string Url);

public class GetGoogleCalendarAuthUrlEndpoint(IGoogleCalendarService googleCalendarService)
    : EndpointWithoutRequest<GoogleCalendarAuthUrlResponse>
{
    public override void Configure()
    {
        Get("user/google-calendar/auth-url");
        Summary(s => { s.Summary = "Get Google Calendar OAuth authorization URL"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var url = googleCalendarService.GetAuthUrl();
        await Send.OkAsync(new GoogleCalendarAuthUrlResponse(url), ct);
    }
}
