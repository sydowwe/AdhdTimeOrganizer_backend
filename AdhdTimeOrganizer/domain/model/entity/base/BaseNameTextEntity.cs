using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.@base;

public abstract class BaseNameTextEntity : BaseEntityWithUser, IEntityWithName
{
    public required string Name { get; set; }

    public string? Text { get; set; }
}