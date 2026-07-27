using System.Security.Claims;
using FastEndpoints;
using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.domain.helper;

/// <summary>
/// Canonical role name groups. Single source of truth for endpoint authorization —
/// the names here must match the roles seeded by the UserRole seeder.
/// </summary>
public static class UserRoles
{
    public static readonly string[] UserOrHigher =
    [
        nameof(UserRoleEnum.User),
        nameof(UserRoleEnum.Admin),
        nameof(UserRoleEnum.Root)
    ];

    public static readonly string[] AdminOrHigher =
    [
        nameof(UserRoleEnum.Admin),
        nameof(UserRoleEnum.Root)
    ];
}

public static class EndpointExtensions
{
    extension(IEndpoint _)
    {
        public static string[] GetUserRole() => UserRoles.UserOrHigher;
        public static string[] GetAdminRole() => UserRoles.AdminOrHigher;
    }

    public static bool IsAdminOrHigher(this ClaimsPrincipal user) =>
        user.IsInRole(nameof(UserRoleEnum.Admin)) || user.IsInRole(nameof(UserRoleEnum.Root));
}