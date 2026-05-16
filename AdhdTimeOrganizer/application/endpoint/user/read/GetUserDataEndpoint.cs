using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

/// <summary>
/// Returns the currently logged-in user's profile data.
/// </summary>
public class GetCurrentUserEndpoint(UserManager<User> userManager, UserMapper mapper)
    : EndpointWithoutRequest<UserDataResponse>
{
    public override void Configure()
    {
        Post("/user/data");
        Summary(s => { s.Summary = "Get full profile data for the authenticated user"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await Send.OkAsync(mapper.ToDataResponse(user), ct);
    }
}
