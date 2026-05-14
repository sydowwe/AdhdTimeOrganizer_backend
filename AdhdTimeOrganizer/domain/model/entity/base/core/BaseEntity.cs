using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.@base.core;

public class BaseEntity : IEntityWithId
{
    public long Id { get; set; }
}