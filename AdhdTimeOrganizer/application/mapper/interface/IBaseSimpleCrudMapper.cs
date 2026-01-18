using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseSimpleCrudMapper<TEntity, in TRequest, out TResponse> : IBaseReadMapper<TEntity, TResponse>, IBaseCreateMapper<TEntity, TRequest>, IBaseUpdateMapper<TEntity, TRequest>
    where TEntity : class, IEntityWithUser
    where TRequest : class, IMyRequest
    where TResponse : class, IMyResponse
{
}