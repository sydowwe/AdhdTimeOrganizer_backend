using Sydowwe.Framework.domain.entity.@base;

namespace Sydowwe.Framework.domain.entity.user;

public abstract class BaseEntityWithUser<TUser> : BaseTableEntity, IEntityWithUser
    where TUser : BaseUser
{
    // Deliberately not `required`: the value is stamped on insert by BaseWithUserEntitySaveChangesAsync,
    // so call sites have nothing meaningful to supply. `required` only forced a placeholder `UserId = 0`
    // - which is exactly the value that produces the FK violation it looked like it was preventing.
    public long UserId { get; set; }
    public TUser User { get; set; } = null!;
}