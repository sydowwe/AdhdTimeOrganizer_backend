namespace AdhdTimeOrganizer.Core.infrastructure.security;

/// <summary>
/// Authorization policy names specific to this deployment.
///
/// <para>The client-type gate itself — <c>ExtensionClientRequirement</c>,
/// <c>ExtensionClientAuthorizationHandler</c>, <c>[AllowExtensionClients]</c> and the
/// <c>DenyExtensionClients</c> / <c>WebOnly</c> / <c>ExtensionOnly</c> policy names — is framework
/// machinery and lives in <c>Sydowwe.Framework.infrastructure.security</c>. What lives here is what
/// names something only this product has.</para>
///
/// <para>⚠ <b>This is the name, not the policy.</b> What the policy actually <em>requires</em> stays
/// host-side, in <c>IdentityServiceExtensions</c> — it names <c>ExtensionRoleClaimsProvider</c>'s role,
/// and deciding which clients may report activity is a product decision the host owns. Only the key
/// both sides have to agree on sits in Core, the same way <see cref="PortalRoleCatalog"/> holds the
/// role names: the host registers the policy, the Tracking slice's endpoints attach it by name, and
/// neither can see the other. It was in the host until the Tracking extraction, when a slice project
/// first needed to name it.</para>
/// </summary>
public static class PortalAuthorizationPolicies
{
    /// <summary>
    /// Gates the activity-tracking endpoints. Requires the <c>ActivityTracking</c> role, which only
    /// extension/desktop tokens receive (see <c>ExtensionRoleClaimsProvider</c>).
    ///
    /// <para>⚠ The policy name and the role name are <b>two different constants that happen to share
    /// this string</b>, and the <c>AutoTagOverride("ActivityTracking")</c> in <c>endpointGroups/</c> is
    /// a third, unrelated use — a Swagger tag. Renaming one renames none of the others.</para>
    /// </summary>
    public const string ActivityTracking = "ActivityTracking";
}
