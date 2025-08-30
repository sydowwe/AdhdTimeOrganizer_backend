namespace AdhdTimeOrganizer.domain.model.entity;

public abstract class BaseTableEntity : BaseEntity
{
    [MapperIgnore] public DateTime CreatedTimestamp { get; set; } = DateTime.UtcNow;
    [MapperIgnore] public DateTime ModifiedTimestamp { get; set; } = DateTime.UtcNow;
}