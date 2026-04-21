using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.googleCalendar;

public record ConnectGoogleCalendarRequest(string Code);

public class ConnectGoogleCalendarEndpoint(
    UserManager<User> userManager,
    IGoogleCalendarService googleCalendarService)
    : Endpoint<ConnectGoogleCalendarRequest>
{
    public override void Configure()
    {
        Post("user/google-calendar/connect");
        Summary(s => { s.Summary = "Connect Google Calendar by exchanging OAuth code for refresh token"; });
    }

    public override async Task HandleAsync(ConnectGoogleCalendarRequest req, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var refreshToken = await googleCalendarService.ExchangeCodeForRefreshToken(req.Code);
        if (refreshToken is null)
        {
            AddError("Failed to obtain refresh token from Google. Ensure access_type=offline and prompt=consent were used.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        user.GoogleCalendarRefreshToken = refreshToken;
        await userManager.UpdateAsync(user);

        await Send.OkAsync(ct);
    }
}
