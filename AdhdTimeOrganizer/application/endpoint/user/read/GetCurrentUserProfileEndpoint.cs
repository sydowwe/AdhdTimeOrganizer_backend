using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

/// <summary>
/// Returns the currently logged-in user's profile data.
/// </summary>
public class GetCurrentUserEndpoint(UserManager<User> userManager, UserMapper mapper)
    : EndpointWithoutRequest<UserResponse>
{
    public override void Configure()
    {
        Get("user/me");
        Summary(s => { s.Summary = "Get current user profile"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var response = mapper.ToResponse(user);
        await SendOkAsync(response, ct);
    }
}
