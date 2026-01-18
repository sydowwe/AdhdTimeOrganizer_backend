using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings._2fa;

/// <summary>
/// Returns the current user's two-factor authentication status.
/// </summary>
public class GetTwoFactorAuthStatusEndpoint(UserManager<User> userManager)
    : EndpointWithoutRequest<bool>
{
    public override void Configure()
    {
        Get("user/2fa/status");
        Summary(s => { s.Summary = "Get two-factor authentication status"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        await SendOkAsync(user.TwoFactorEnabled, ct);
    }
}
