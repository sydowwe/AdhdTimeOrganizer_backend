using System.Security.Claims;

namespace Sydowwe.Framework.domain.extServiceContract.user;

public interface ILoggedUserService
{
    ClaimsPrincipal? LoggedUserPrincipal { get; }
    bool IsAuthenticated { get; }
    IEnumerable<string> GetRoles { get; }
    string GetEmail { get; }
    long GetUserId { get; }
    long? GetUserIdOrNull { get; }
}