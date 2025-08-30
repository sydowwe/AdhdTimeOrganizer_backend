using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseCrudMapper<TEntity, in TRequest, out TResponse> : IBaseReadMapper<TEntity, TResponse>
    where TEntity : BaseTableEntity
    where TRequest : class, IMyRequest
    where TResponse : class, IMyResponse
{
    public TEntity ToEntity(TRequest request);
    public TEntity UpdateEntity(TRequest request, TEntity entity);
}