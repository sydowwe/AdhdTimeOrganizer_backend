namespace AdhdTimeOrganizer.domain.model.entityInterface;

public interface IEntityWithIsHidden : IEntityWithId
{
    public bool IsHidden { get; set; }
}