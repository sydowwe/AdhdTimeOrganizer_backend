using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.@base;

public abstract class BaseEntityWithIsDone : BaseEntityWithActivity, IEntityWithIsDone
{
    public bool IsDone { get; set; }
}