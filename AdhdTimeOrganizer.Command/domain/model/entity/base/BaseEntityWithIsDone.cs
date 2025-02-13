using AdhdTimeOrganizer.Command.domain.model.entity.activity;

namespace AdhdTimeOrganizer.Command.domain.model.entity.@base;

public abstract class BaseEntityWithIsDone : BaseEntityWithActivity
{
    public bool IsDone { get; set; } = false;
}