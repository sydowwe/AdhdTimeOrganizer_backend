using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.googleCalendar;

public class DisconnectGoogleCalendarEndpoint(UserManager<User> userManager)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("user/google-calendar/disconnect");
        Summary(s => { s.Summary = "Disconnect Google Calendar integration"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        user.GoogleCalendarRefreshToken = null;
        await userManager.UpdateAsync(user);

        await Send.OkAsync(ct);
    }
}
