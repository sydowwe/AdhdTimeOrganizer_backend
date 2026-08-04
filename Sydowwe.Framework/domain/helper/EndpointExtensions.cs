using System.Security.Claims;
using FastEndpoints;
using Sydowwe.Framework.domain.auth;

namespace Sydowwe.Framework.domain.helper;

/// <summary>
/// Role gates for endpoints, resolved from the deployment's <see cref="IRoleCatalog"/> rather than
/// from a role enum owned by this assembly - see <see cref="RoleTier"/> for why.
///
/// <para>Only the two tiers every deployment is guaranteed to have live here. A middle tier is
/// deployment-specific (this solution's HR tier, for example) and its helper belongs next to the
/// host's own role type, not in shared infrastructure.</para>
///
/// <para>Declared as classic <c>this</c>-parameter extension methods on purpose. A C# 14
/// <c>extension(IEndpoint _)</c> block declares <i>instance</i> extension members, which cannot be
/// invoked on the interface type itself - that form needs a receiver-less <c>extension(IEndpoint)</c>
/// block. Mixing the two is a compile error (CS0120), so the plain form stays until there is a
/// reason to move.</para>
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Every authenticated role - the widest gate. Only use it on an endpoint that also scopes rows
    /// to the caller; the base read endpoints do NOT do that by default (see <c>ApplyUserScoping</c>).
    /// </summary>
    public static string[] GetUserRole(this IEndpoint _) => FrameworkRoles.AtOrAbove(RoleTier.User);

    /// <summary>Administrators and above.</summary>
    public static string[] GetAdminRole(this IEndpoint _) => FrameworkRoles.AtOrAbove(RoleTier.Admin);

    /// <summary>
    /// The gate every base endpoint falls back to when it does not override <c>AllowedRoles()</c>,
    /// resolved from <see cref="FrameworkRoles.DefaultEndpointTier"/> — Admin unless the host widened
    /// it at startup. Base classes call this rather than naming a tier directly so that the choice
    /// lives in one host-owned line instead of in sixteen shared files.
    /// </summary>
    public static string[] GetDefaultRoles(this IEndpoint _) =>
        FrameworkRoles.AtOrAbove(FrameworkRoles.DefaultEndpointTier);

    public static bool IsAdminOrHigher(this ClaimsPrincipal user) =>
        FrameworkRoles.AtOrAbove(RoleTier.Admin).Any(user.IsInRole);
}
