using AdhdTimeOrganizer.Common.domain.model.entityInterface;

namespace AdhdTimeOrganizer.Common.domain.model.entity;

public abstract class BaseEntity : IBaseEntity
{
    public long Id { get; set; }
    public DateTime CreatedTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedTimestamp { get; set; } = DateTime.UtcNow;
}