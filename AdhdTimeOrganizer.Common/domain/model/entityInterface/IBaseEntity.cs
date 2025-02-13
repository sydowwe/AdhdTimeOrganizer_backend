namespace AdhdTimeOrganizer.Common.domain.model.entityInterface;

public interface IBaseEntity : IEntityWithId
{
    DateTime CreatedTimestamp { get; set; }
    DateTime ModifiedTimestamp { get; set; }
}