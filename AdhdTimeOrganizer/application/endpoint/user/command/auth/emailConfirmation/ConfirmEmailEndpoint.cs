using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.emailConfirmation;

public class ConfirmEmailEndpoint(UserManager<User> userManager) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("auth/confirm-email");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Query<long>("userId");
        var token = Query<string>("token");

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
        {
            await Send.OkAsync("Email confirmed successfully", ct);
        }
        else
        {
            await Send.ResponseAsync("Invalid or expired confirmation link", 400, ct);
        }
    }
}