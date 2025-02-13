using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.model.entityInterface;

namespace AdhdTimeOrganizer.Common.application.@interface;

public interface IBaseService<TEntity>
    where TEntity : IEntity
{
}