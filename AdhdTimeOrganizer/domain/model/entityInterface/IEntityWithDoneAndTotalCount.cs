namespace AdhdTimeOrganizer.domain.model.entityInterface;

public interface IEntityWithDoneAndTotalCount
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
}