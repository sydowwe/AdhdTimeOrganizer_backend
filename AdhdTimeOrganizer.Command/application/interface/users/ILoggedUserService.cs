using System.Security.Claims;

namespace AdhdTimeOrganizer.Command.application.@interface.users;

public interface ILoggedUserService
{
    ClaimsPrincipal? LoggedUserPrincipal { get; }
    bool IsAuthenticated { get; }
    IEnumerable<string> GetRoles { get; }
    string GetEmail { get; }
    long GetUserId { get; }
}