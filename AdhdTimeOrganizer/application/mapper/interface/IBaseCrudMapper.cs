using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseCrudMapper<TEntity, in TCreateRequest, in TUpdateRequest, out TResponse> : IBaseResponseMapper<TEntity, TResponse>, IBaseCreateMapper<TEntity, TCreateRequest>, IBaseUpdateMapper<TEntity, TUpdateRequest>
    where TEntity : class, IEntityWithUser, IEntityWithId
    where TCreateRequest : class, ICreateRequest
    where TUpdateRequest : class, IUpdateRequest
    where TResponse : class, IMyResponse
{
}