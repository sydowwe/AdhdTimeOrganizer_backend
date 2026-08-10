using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entityInterface;

namespace AdhdTimeOrganizer.Core.domain.model.entity.@base;

public abstract class BaseEntityWithIsDone : BaseEntityWithActivity, IEntityWithIsDone
{
    public bool IsDone { get; set; }
}