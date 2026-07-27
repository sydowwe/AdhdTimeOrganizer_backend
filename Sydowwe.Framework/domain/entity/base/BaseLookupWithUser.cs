using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.domain.entity.@base;

/// <summary>
/// A lookup ("reference"/"code") table whose values are owned per user — each user has their own
/// set, unique on <c>(UserId, Text)</c>. <see cref="BaseLookup"/> is the host-wide equivalent; the
/// two share no base because only this one carries a user, so <see cref="IBaseLookupEntity"/> is
/// what generic lookup DTOs bind to.
/// </summary>
public abstract class BaseLookupWithUser<TUser> : BaseEntityWithUser<TUser>, IBaseLookupEntity
    where TUser : BaseUser
{
    // Not `required`: LookupRequest<TEntity> constructs these through a `new()` constraint, which
    // C# forbids on types with required members.
    public string Text { get; set; } = null!;
    public int? SortOrder { get; set; }
}
