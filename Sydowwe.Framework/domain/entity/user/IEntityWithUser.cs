using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.domain.entity.user;

public interface IEntityWithUser : IEntityWithId
{
    public long UserId { get; set; }
}