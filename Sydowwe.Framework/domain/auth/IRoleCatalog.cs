namespace Sydowwe.Framework.domain.auth;

/// <summary>
/// One role as the deployment defines it. Carries everything the framework needs to gate an endpoint
/// on it and to seed it — and nothing about what the role <i>means</i>, which stays with the host.
/// </summary>
/// <param name="Name">
/// The role name as it appears in the <c>user_role</c> table and in the <c>role</c> claim. This
/// string is a storage contract: it is persisted, and in this solution it is also persisted on
/// business tables. Renaming one is a data migration, never just a code change.
/// </param>
/// <param name="Tier">Where the role sits in the privilege ordering.</param>
/// <param name="Description">Human-readable purpose, stored on the role row.</param>
/// <param name="IsDefault">Whether new accounts get this role when none is specified.</param>
/// <param name="IsAssignable">Whether the role may be granted through the UI (false for root).</param>
public record RoleDefinition(
    string Name,
    RoleTier Tier,
    string Description,
    bool IsDefault = false,
    bool IsAssignable = true);

/// <summary>
/// The host's answer to "what roles exist, and how do they rank". Supplied once per deployment and
/// consumed by the framework wherever it needs role <i>names</i>: endpoint gating
/// (<c>EndpointExtensions</c>), role seeding (<c>UserRoleSeeder</c>) and default role assignment on
/// registration.
///
/// <para>Implement it next to the host's own role type — that is the only place that knows both the
/// names and what they mean.</para>
/// </summary>
public interface IRoleCatalog
{
    /// <summary>Every role in the deployment, ascending by tier.</summary>
    IReadOnlyList<RoleDefinition> All { get; }

    /// <summary>
    /// The names of every role at or above <paramref name="tier"/> — the cumulative array an endpoint
    /// passes to <c>Roles(...)</c>. Returns an empty array if the deployment has nothing at or above
    /// that tier, which correctly denies everyone rather than accidentally allowing anyone.
    /// </summary>
    string[] AtOrAbove(RoleTier tier);

    /// <summary>
    /// The role a newly registered account receives. Throws if the catalog declares no default —
    /// deliberately fatal at startup rather than silently creating role-less accounts that fail
    /// every authorization check with no obvious cause.
    /// </summary>
    string DefaultRoleName { get; }
}