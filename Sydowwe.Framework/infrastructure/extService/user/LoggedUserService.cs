using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace Sydowwe.Framework.infrastructure.extService.user;

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

    public long GetUserId =>
        LoggedUserPrincipal?.GetId()
        ?? throw new InvalidOperationException("Not authenticated cannot get logged user id");

    public long? GetUserIdOrNull => LoggedUserPrincipal?.GetIdOrNull();
}