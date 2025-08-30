using System.Security.Claims;

namespace AdhdTimeOrganizer.domain.extServiceContract.user;

public interface ILoggedUserService
{
    ClaimsPrincipal? LoggedUserPrincipal { get; }
    bool IsAuthenticated { get; }
    IEnumerable<string> GetRoles { get; }
    string GetEmail { get; }
    long GetUserId { get; }

    // Task<User> GetCurrentUserAsync();
}