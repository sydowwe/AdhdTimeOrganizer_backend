using AdhdTimeOrganizer.domain.model.entityInterface;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.domain.model.entity;

public abstract class BaseTableEntity : BaseEntity, IBaseTableEntity
{
    [MapperIgnore] public DateTime CreatedTimestamp { get; set; } = DateTime.UtcNow;
    [MapperIgnore] public DateTime ModifiedTimestamp { get; set; } = DateTime.UtcNow;
}