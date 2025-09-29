using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class ConfirmEmailEndpoint(UserManager<User> userManager) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/user/confirm-email");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Query<long>("userId");
        var token = Query<string>("token");

        if (userId <= 0 || string.IsNullOrEmpty(token))
        {
            await SendAsync("UserId and token must be supplied", 400, ct);
            return;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendAsync("User not found", 404, ct);
            return;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);

        if (result.Succeeded)
        {
            await SendOkAsync("Email confirmed successfully", ct);
        }
        else
        {
            var errorMessages = string.Join(", ", result.Errors.Select(e => e.Description));
            await SendAsync($"Failed to confirm email: {errorMessages}", 422, ct);
        }
    }
}