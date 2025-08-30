using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public abstract class BaseEntityWithActivity : BaseEntityWithUser
{
    public long ActivityId { get; set; }
    public virtual Activity Activity { get; set; }
}