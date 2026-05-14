using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.@base.core;

public abstract class SelectOptionBase : BaseEntityWithUser
{
    public required string Text { get; set; }
    public required int SortOrder { get; set; }
}