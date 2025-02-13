using AdhdTimeOrganizer.Common.domain.model.entity;

namespace AdhdTimeOrganizer.Command.domain.model.entity.user;

public abstract class BaseEntityWithUser : BaseEntity, IBaseEntityWithUser
{
    public long UserId { get; set; }
    public UserEntity User { get; set; }
}