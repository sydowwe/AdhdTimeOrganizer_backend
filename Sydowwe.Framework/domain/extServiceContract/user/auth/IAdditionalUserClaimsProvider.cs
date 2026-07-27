using System.Security.Claims;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.domain.extServiceContract.user.auth;

/// <summary>
/// Extension seam for contributing deployment-specific claims to the access token at mint time
/// (login and every refresh). <see cref="IJwtService{TUser}"/> resolves these as a collection, so a
/// deployment can register zero or more providers without the framework knowing what they add (e.g. a
/// Core module adding the caller's <c>employee_id</c>, or granting an extension-only role via
/// <paramref name="clientType"/>). Keep implementations cheap — they run on every token mint — and only
/// cache values that are immutable for the token's lifetime.
/// </summary>
public interface IAdditionalUserClaimsProvider<in TUser> where TUser : BaseUser
{
    Task<IEnumerable<Claim>> GetClaimsAsync(TUser user, ClientTypeEnum clientType);
}