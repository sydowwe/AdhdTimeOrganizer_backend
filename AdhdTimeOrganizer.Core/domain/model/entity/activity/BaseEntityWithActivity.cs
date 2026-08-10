using AdhdTimeOrganizer.Core.domain.model.entity.user;

namespace AdhdTimeOrganizer.Core.domain.model.entity.activity;

public abstract class BaseEntityWithActivity : BaseEntityWithUser
{
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;
}