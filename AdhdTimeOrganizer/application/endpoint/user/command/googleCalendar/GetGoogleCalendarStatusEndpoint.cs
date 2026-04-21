using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.googleCalendar;

public record GoogleCalendarStatusResponse(bool Connected);

public class GetGoogleCalendarStatusEndpoint(UserManager<User> userManager)
    : EndpointWithoutRequest<GoogleCalendarStatusResponse>
{
    public override void Configure()
    {
        Get("user/google-calendar/status");
        Summary(s => { s.Summary = "Get Google Calendar connection status"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await Send.OkAsync(new GoogleCalendarStatusResponse(user.GoogleCalendarRefreshToken is not null), ct);
    }
}
