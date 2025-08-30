using System.Security.Claims;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user;

namespace AdhdTimeOrganizer.infrastructure.extService.user;

public class LoggedUserService(IHttpContextAccessor httpContextAccessor) : ILoggedUserService, IScopedService
{
    public ClaimsPrincipal? LoggedUserPrincipal =>
        httpContextAccessor.HttpContext?.User.Identity is not { IsAuthenticated: true }
            ? null
            : httpContextAccessor.HttpContext.User;

    public bool IsAuthenticated => LoggedUserPrincipal != null;

    public IEnumerable<string> GetRoles =>
        LoggedUserPrincipal?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? [];

    public string GetEmail =>
        LoggedUserPrincipal?.FindFirst(ClaimTypes.Email)?.Value ??
        throw new InvalidOperationException("Missing email in claims");

    public long GetUserId => IsAuthenticated
        ? long.Parse(LoggedUserPrincipal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("Id missing in claims"))
        : throw new InvalidOperationException("Not authenticated cannot get logged user id");


    // public async Task<User> GetCurrentUserAsync()
    // {
    //     var principal = LoggedUserPrincipal;
    //     if (principal == null)
    //     {
    //         throw new NullReferenceException("Logged user principal is null");
    //     }
    //
    //     var user = await userManager.GetUserAsync(principal);
    //     if (user == null) throw new UserByPrincipalNotFoundException(principal);
    //     return user;
    // }
}