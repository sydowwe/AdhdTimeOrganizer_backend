using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity;

public abstract class BaseTodoList : BaseEntityWithIsDone, IEntityWithDoneAndTotalCount
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public long DisplayOrder { get; set; }
}