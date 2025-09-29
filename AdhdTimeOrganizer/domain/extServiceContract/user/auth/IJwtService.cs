using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface IJwtService
{
    Task GenerateJwtAndSetAuthCookie(bool stayLoggedIn, AuthMethodEnum authMethod, User user, UserManager<User> userManager, HttpContext httpContext);
}