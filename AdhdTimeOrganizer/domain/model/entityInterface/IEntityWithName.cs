namespace AdhdTimeOrganizer.domain.model.entityInterface;

public interface IEntityWithName : IEntity
{
    public string Name { get; set; }
}