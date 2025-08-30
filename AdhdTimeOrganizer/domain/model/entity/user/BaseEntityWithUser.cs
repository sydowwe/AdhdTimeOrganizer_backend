namespace AdhdTimeOrganizer.domain.model.entity.user;

public abstract class BaseEntityWithUser : BaseTableEntity, IEntityWithUser
{
    public required long UserId { get; set; }
    public User User { get; set; } = null!;
}