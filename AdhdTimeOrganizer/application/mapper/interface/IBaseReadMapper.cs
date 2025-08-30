using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseReadMapper<in TEntity, out TResponse> : IBaseResponseMapper<TEntity, TResponse>, IBaseSelectOptionMapper<TEntity>
    where TEntity : class, IEntityWithId
    where TResponse : class, IMyResponse
{
}