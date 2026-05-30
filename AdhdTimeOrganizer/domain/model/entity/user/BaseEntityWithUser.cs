using AdhdTimeOrganizer.domain.model.entity.@base.core;

namespace AdhdTimeOrganizer.domain.model.entity.user;

public abstract class BaseEntityWithUser : BaseTableEntity, IEntityWithUser
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
}