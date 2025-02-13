namespace AdhdTimeOrganizer.Common.domain.model.entityInterface;

public interface IEntityWithId : IEntity
{
    long Id { get; set; }
}