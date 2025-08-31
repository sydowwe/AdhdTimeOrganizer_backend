using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.@base;

public interface IEntityWithIsDone : IEntityWithId
{
    public bool IsDone { get; set; }
}