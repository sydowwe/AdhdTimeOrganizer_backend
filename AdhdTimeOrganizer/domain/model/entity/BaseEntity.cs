using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity;

public class BaseEntity : IEntityWithId
{
    public long Id { get; set; }
}