using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.googleCalendar;

/// <param name="Configured">
/// Whether this deployment can run the OAuth flow at all. <c>false</c> → the client hides <i>Connect</i>
/// and words the card as "not available here"; <c>GET …/auth-url</c> would answer 503 anyway (B14).
/// </param>
/// <param name="Connected">Whether the caller has a stored Google refresh token.</param>
public record GoogleCalendarStatusResponse(bool Configured, bool Connected);

public class GetGoogleCalendarStatusEndpoint(
    UserManager<User> userManager,
    IGoogleCalendarService googleCalendarService)
    : EndpointWithoutRequest<GoogleCalendarStatusResponse>
{
    public override void Configure()
    {
        Get("/user/google-calendar/status");
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

        await Send.OkAsync(new GoogleCalendarStatusResponse(
            Configured: googleCalendarService.IsConfigured,
            Connected: user.GoogleCalendarRefreshToken is not null), ct);
    }
}
