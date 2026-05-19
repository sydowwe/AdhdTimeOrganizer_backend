using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.user;

public interface IEntityWithUser
{
    public long UserId { get; set; }
    public User User { get; set; }
}