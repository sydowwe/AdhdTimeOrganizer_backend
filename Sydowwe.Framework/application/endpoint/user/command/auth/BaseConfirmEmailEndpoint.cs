using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.domain.entity.user;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Confirms a newly registered address from the link mailed to it. Anonymous by necessity — the
/// caller has no session yet, the token is the only credential.
///
/// <para>Both "no such user" and "bad token" answer with the same 400 and the same message, so the
/// route cannot be used to probe which user ids exist.</para>
/// </summary>
public abstract class BaseConfirmEmailEndpoint<TUser>(UserManager<TUser> userManager)
    : Endpoint<ConfirmEmailRequest>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Post("/auth/confirm-email");
        AllowAnonymous();
        Throttle(30, 60, "X-Forwarded-For");
        Validator<ConfirmEmailValidator>();
    }

    public override async Task HandleAsync(ConfirmEmailRequest req, CancellationToken ct)
    {
        var userId = req.UserId;
        var token = req.Token;

        if (userId <= 0 || string.IsNullOrEmpty(token))
        {
            await Send.ResponseAsync("UserId and token must be supplied", 400, ct);
            return;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await Send.ResponseAsync("Invalid or expired confirmation link", 400, ct);
            return;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);

        if (result.Succeeded)
            await Send.OkAsync("Email confirmed successfully", ct);
        else
            await Send.ResponseAsync("Invalid or expired confirmation link", 400, ct);
    }
}
