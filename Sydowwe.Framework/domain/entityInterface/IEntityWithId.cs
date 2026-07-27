namespace Sydowwe.Framework.domain.entityInterface;

public interface IEntityWithId : IEntity
{
    long Id { get; set; }
}