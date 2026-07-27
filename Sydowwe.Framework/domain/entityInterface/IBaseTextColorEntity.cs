namespace Sydowwe.Framework.domain.entityInterface;

public interface IBaseTextColorEntity : IBaseTableEntity
{
    public string Text { get; set; }
    public string Color { get; set; }
}