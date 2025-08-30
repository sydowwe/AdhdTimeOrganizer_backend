namespace AdhdTimeOrganizer.domain.model.entityInterface;

public interface IBaseTableEntity : IEntityWithId
{
    public DateTime CreatedTimestamp { get; set; }
    public DateTime ModifiedTimestamp { get; set; }
}