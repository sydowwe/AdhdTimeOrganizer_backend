namespace Sydowwe.Framework.domain.entityInterface;

public interface IEntityWithIsHidden : IEntityWithId
{
    public bool IsHidden { get; set; }
}