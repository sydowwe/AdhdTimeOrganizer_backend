using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.user;

public interface IBaseEntityWithUser : IEntityWithId
{
    public long UserId { get; set; }
    public User User { get; set; }
}