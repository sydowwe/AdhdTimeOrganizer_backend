using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.model.valueObject;
using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Common.application.@interface;

public interface IBaseReadService<TEntity, TResponse>: IBaseService<TEntity>
    where TEntity : BaseEntity
    where TResponse : IMyResponse
{
    Task<ServiceResult<TResponse>> GetByIdAsResponseAsync(long id);
    Task<ServiceResult<TEntity>> GetByIdAsync(long id);
    Task<List<TResponse>> GetAllAsync();
    Task<List<SelectOptionResponse>> GetAllAsOptionsAsync();
}