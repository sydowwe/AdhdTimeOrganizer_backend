using AdhdTimeOrganizer.Common.domain.model.entityInterface;

namespace AdhdTimeOrganizer.Command.domain.model.entity.user;

public interface IBaseEntityWithUser : IBaseEntity
{
    public long UserId { get; set; }
    public UserEntity User { get; set; }
}