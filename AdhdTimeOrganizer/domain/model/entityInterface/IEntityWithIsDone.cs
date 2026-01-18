namespace AdhdTimeOrganizer.domain.model.entityInterface;

public interface IEntityWithIsDone : IEntityWithId
{
    public bool IsDone { get; set; }
}