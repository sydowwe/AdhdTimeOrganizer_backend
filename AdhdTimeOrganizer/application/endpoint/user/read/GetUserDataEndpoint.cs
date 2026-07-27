using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.read;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

/// <summary>
/// Returns the currently logged-in user's profile data.
/// </summary>
public class GetCurrentUserEndpoint(UserManager<User> userManager)
    : BaseGetCurrentUserEndpoint<User>(userManager)
{
}