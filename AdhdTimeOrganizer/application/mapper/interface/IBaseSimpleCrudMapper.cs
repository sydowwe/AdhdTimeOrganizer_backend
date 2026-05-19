using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseSimpleCrudMapper<TEntity, in TRequest, out TResponse> : IBaseResponseMapper<TEntity, TResponse>, IBaseCreateMapper<TEntity, TRequest>, IBaseUpdateMapper<TEntity, TRequest>
    where TEntity : class, IEntityWithUser, IEntityWithId
    where TRequest : class, IMyRequest
    where TResponse : class, IMyResponse
{
}