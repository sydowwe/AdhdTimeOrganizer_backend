using Sydowwe.Framework.domain.auth;

namespace AdhdTimeOrganizer.infrastructure.security;

/// <summary>
/// This deployment's answer to "what roles exist, and how do they rank" — the single source both
/// endpoint gating (<c>EndpointExtensions</c>) and <c>UserRoleSeeder</c> read from. Installed in
/// <c>Program.ConfigureServices</c> via <see cref="FrameworkRoles.Configure"/>, before
/// <c>AddFastEndpoints()</c>, because endpoint <c>Configure()</c> reads it during registration.
///
/// <para><b>Default endpoint tier is <see cref="RoleTier.User"/>, deliberately.</b> Every account in
/// this app is a plain <c>User</c>; the framework's deny-by-default Admin tier would make every base
/// endpoint that doesn't override <c>AllowedRoles()</c> unreachable. What keeps other users' rows out
/// here is not the role gate but <c>AppDbContext</c>'s per-user global query filters — see the
/// user-scoping notes in CLAUDE.md. Module reads have no such net and must scope themselves.</para>
/// </summary>
public static class PortalRoleCatalog
{
    // The names are a persisted contract (they are the AspNetRoles rows), so they live here as
    // constants rather than being retyped at each use site. They used to come from Framework's
    // UserRoleEnum; that enum is gone because role names are deployment-specific, and this catalog
    // is now the one place this deployment declares them.
    public const string User = "User";
    public const string Admin = "Admin";
    public const string Root = "Root";

    public static IRoleCatalog Create() => new RoleCatalog(
    [
        new RoleDefinition(User, RoleTier.User, "Standard user", IsDefault: true),
        new RoleDefinition(Admin, RoleTier.Admin, "Administrator"),
        // Seeded, never granted through the UI.
        new RoleDefinition(Root, RoleTier.Root, "Root administrator", IsAssignable: false)
    ]);
}
