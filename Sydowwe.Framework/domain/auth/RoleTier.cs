namespace Sydowwe.Framework.domain.auth;

/// <summary>
/// The framework's only model of privilege: an ordered set of tiers, with no names attached.
///
/// <para><b>Why tiers instead of a role enum.</b> A role <i>catalog</i> is the most
/// deployment-specific thing in an authorization system — one app's <c>Employee</c>/<c>Hr</c> is
/// another's <c>User</c>, and a framework that hard-codes either forces every other app to adopt it.
/// Worse, in this solution role names are also a <b>persisted domain value</b> (<c>job_title.role</c>,
/// checklist assignee columns, stored as text via <c>EnumColumn</c>), so the naming belongs to the
/// app that owns those tables, not to shared infrastructure.</para>
///
/// <para>What the framework genuinely needs is only the <i>ordering</i> — "this endpoint is for
/// admins and above". Hosts map their own roles onto these tiers through
/// <see cref="IRoleCatalog"/>. A host with no middle tier simply maps nothing to
/// <see cref="Elevated"/>; <c>AtOrAbove(Elevated)</c> then returns its admin tiers, which is the
/// correct answer for that deployment.</para>
///
/// <para>Values are explicit and ascending because comparison is the whole point. Insert new tiers
/// with gaps if ever needed — do not renumber, since a host may persist the level (see
/// <c>UserRole.RoleLevel</c>).</para>
/// </summary>
public enum RoleTier
{
    /// <summary>Ordinary authenticated user. The lowest tier that can reach an authorized endpoint.</summary>
    User = 1,

    /// <summary>
    /// A privileged non-admin tier, for deployments that have one (e.g. HR in an employee-records
    /// app). Deployments without one map no role here.
    /// </summary>
    Elevated = 2,

    /// <summary>Administrator of a deployment.</summary>
    Admin = 3,

    /// <summary>Highest privilege; typically seeded, not assignable through the UI.</summary>
    Root = 4
}