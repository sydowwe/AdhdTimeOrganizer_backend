namespace AdhdTimeOrganizer.infrastructure.security;

/// <summary>
/// Authorization policy names specific to this deployment.
///
/// <para>The client-type gate itself — <c>ExtensionClientRequirement</c>,
/// <c>ExtensionClientAuthorizationHandler</c>, <c>[AllowExtensionClients]</c> and the
/// <c>DenyExtensionClients</c> / <c>WebOnly</c> / <c>ExtensionOnly</c> policy names — is framework
/// machinery and lives in <c>Sydowwe.Framework.infrastructure.security</c>. What stays here is what
/// names something only this product has.</para>
/// </summary>
public static class PortalAuthorizationPolicies
{
    /// <summary>
    /// Gates the activity-tracking endpoints. Requires the <c>ActivityTracking</c> role, which only
    /// extension/desktop tokens receive (see <c>ExtensionRoleClaimsProvider</c>) — a product decision
    /// about which clients may report activity, which is why neither the policy nor the role name is
    /// a framework concern.
    /// </summary>
    public const string ActivityTracking = "ActivityTracking";
}
