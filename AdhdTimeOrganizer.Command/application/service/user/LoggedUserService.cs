using System.Security.Claims;
using AdhdTimeOrganizer.Command.application.@interface.users;
using Microsoft.AspNetCore.Http;

namespace AdhdTimeOrganizer.Command.application.service.user;

public class LoggedUserService(IHttpContextAccessor httpContextAccessor) : ILoggedUserService
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
}