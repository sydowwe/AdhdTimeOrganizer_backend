using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.@base.core;

public abstract class BaseLookup : BaseEntityWithUser
{
    public string Text { get; set; }
    public int? SortOrder { get; set; }
}