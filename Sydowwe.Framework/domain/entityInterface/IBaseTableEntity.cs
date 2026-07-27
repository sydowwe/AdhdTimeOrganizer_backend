namespace Sydowwe.Framework.domain.entityInterface;

public interface IBaseTableEntity : IEntityWithId
{
    public DateTime CreatedTimestamp { get; set; }
    public DateTime ModifiedTimestamp { get; set; }
}