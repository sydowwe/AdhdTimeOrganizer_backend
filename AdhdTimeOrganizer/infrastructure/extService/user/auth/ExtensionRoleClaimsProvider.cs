using System.Security.Claims;
using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

/// <summary>
/// Grants the extension-only <c>ActivityTracking</c> role to tokens minted for a browser-extension
/// client. This lives here rather than in <c>JwtService</c> so the framework never has to know this
/// deployment's role names — it just runs whatever providers the host registers, on login and on
/// every refresh alike.
/// <para>The matching authorization policy is in <c>IdentityServiceExtensions</c>; a web (cookie)
/// token deliberately never carries this role, which is what keeps the tracking endpoints closed to
/// the SPA.</para>
/// <para><b>Trust boundary:</b> this provider grants the role purely on the <paramref name="clientType"/>
/// it is handed — it does no independent verification. Safe today because every call site
/// (<c>JwtService.CreateUserClaims</c>) passes a hard-coded <see cref="ClientTypeEnum"/> literal chosen
/// server-side at the token-minting call site, never a value derived from client-supplied input. If a
/// future caller ever computed <c>clientType</c> from a header or claim instead, this provider would
/// silently mint the elevated role for a web token — keep that invariant true at the call sites, not
/// here.</para>
/// </summary>
public class ExtensionRoleClaimsProvider : IAdditionalUserClaimsProvider<User>, IScopedService
{
    public const string ExtensionRole = "ActivityTracking";

    public Task<IEnumerable<Claim>> GetClaimsAsync(User user, ClientTypeEnum clientType)
    {
        IEnumerable<Claim> claims = clientType == ClientTypeEnum.Extension
            ? [new Claim(ClaimTypes.Role, ExtensionRole)]
            : [];

        return Task.FromResult(claims);
    }
}