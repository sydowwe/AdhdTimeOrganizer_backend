using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.@base.core;

public abstract class BaseTableEntity : BaseEntity, IBaseTableEntity
{
    public DateTime CreatedTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedTimestamp { get; set; } = DateTime.UtcNow;
}