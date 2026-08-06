using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.domain.entity.user;

namespace Sydowwe.Framework.application.endpoint.user.command.twoFactor;

/// <summary>
/// Returns the current user's two-factor authentication status.
///
/// <para>The only one of the 2FA settings endpoints without
/// <see cref="preprocessor.VerifyUserPreProcessor{TUser,TRequest}"/>: it is a plain read that changes
/// nothing and leaks nothing beyond a boolean the caller already owns. Don't add step-up verification
/// here — the SPA calls it to decide whether to even show the 2FA form.</para>
/// </summary>
public abstract class BaseGetTwoFactorAuthStatusEndpoint<TUser>(UserManager<TUser> userManager)
    : EndpointWithoutRequest<bool>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Get("/user/2fa/status");
        Summary(s => { s.Summary = "Get two-factor authentication status"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await Send.OkAsync(user.TwoFactorEnabled, ct);
    }
}