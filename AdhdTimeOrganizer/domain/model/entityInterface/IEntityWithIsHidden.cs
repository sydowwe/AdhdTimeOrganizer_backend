using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.@base;

public interface IEntityWithIsHidden : IEntityWithId
{
    public bool IsHidden { get; set; }
}