using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.@base;

public abstract class BaseEntityWithIsDone : BaseEntityWithActivity
{
    public bool IsDone { get; set; } = false;
}