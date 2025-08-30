namespace AdhdTimeOrganizer.domain.model.entity;

public abstract class SelectOptionBase : BaseTableEntity
{
    public required string Text { get; set; }
    public required int SortOrder { get; set; }
}