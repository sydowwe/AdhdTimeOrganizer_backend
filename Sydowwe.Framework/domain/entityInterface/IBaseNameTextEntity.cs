namespace Sydowwe.Framework.domain.entityInterface;

public interface IBaseNameTextEntity : IBaseTableEntity
{
    public string Name { get; set; }

    public string? Text { get; set; }
}