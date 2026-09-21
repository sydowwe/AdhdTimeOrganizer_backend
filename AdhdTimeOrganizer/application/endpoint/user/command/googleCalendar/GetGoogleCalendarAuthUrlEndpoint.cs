using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using FastEndpoints;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.result;

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
        // A URL the server knows cannot work must not be a 200: without a client id Google answers
        // "Missing required parameter: client_id" on its own error page. The status endpoint carries the
        // same fact as `configured`, so a client that reads it never gets here.
        if (!googleCalendarService.IsConfigured)
        {
            AddError("Google Calendar is not available in this deployment.");
            await Send.ErrorsAsync(this.ErrorStatus(ResultErrorType.ServiceNotConfigured), ct);
            return;
        }

        var state = googleCalendarService.GenerateState();
        HttpContext.Response.Cookies.Append(GoogleCalendarStateCookie.Name, state, GoogleCalendarStateCookie.Options(HttpContext.Request));

        var url = googleCalendarService.GetAuthUrl(state);
        await Send.OkAsync(new GoogleCalendarAuthUrlResponse(url), ct);
    }
}
