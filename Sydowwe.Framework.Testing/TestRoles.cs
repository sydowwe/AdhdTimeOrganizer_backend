using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.Testing;

/// <summary>Role-claim sets handed to <see cref="RoleTestAuthHandler"/> via the test factory.</summary>
public static class TestRoles
{
    public static readonly string[] AdminAndUser = [nameof(UserRoleEnum.Admin), nameof(UserRoleEnum.User)];
    public static readonly string[] Admin = [nameof(UserRoleEnum.Admin)];
    public static readonly string[] User = [nameof(UserRoleEnum.User)];
    public static readonly string[] Root = [nameof(UserRoleEnum.Root)];
}