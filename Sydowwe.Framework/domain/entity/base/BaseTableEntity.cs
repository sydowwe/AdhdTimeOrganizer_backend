using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.domain.entity.@base;

public abstract class BaseTableEntity : BaseEntity, IBaseTableEntity
{
    public DateTime CreatedTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedTimestamp { get; set; } = DateTime.UtcNow;
}