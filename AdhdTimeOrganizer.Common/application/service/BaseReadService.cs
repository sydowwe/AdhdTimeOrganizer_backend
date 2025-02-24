using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.model.valueObject;
using AdhdTimeOrganizer.Common.domain.repositoryContract;
using AdhdTimeOrganizer.Common.domain.result;
using AutoMapper;

namespace AdhdTimeOrganizer.Common.application.service;

public class BaseReadService<TEntity, TResponse,TRepository>(TRepository repository, IMapper mapper) : BaseService<TEntity>(mapper), IBaseReadService<TEntity, TResponse>
    where TEntity : BaseEntity
    where TResponse : class, IMyResponse
    where TRepository : IBaseReadRepository<TEntity>
{
    protected TRepository _repository = repository;

    public async Task<ServiceResult<TResponse>> GetByIdAsResponseAsync(long id)
    {
        var result = await _repository.GetByIdAsync(id);
        return ProcessRepositoryResult<TResponse>(result);
    }
    public async Task<ServiceResult<TEntity>> GetByIdAsync(long id)
    {
        var result = await _repository.GetByIdAsync(id, false);
        return ProcessRepositoryResultWithEntity(result);
    }

    public virtual async Task<List<TResponse>> GetAllAsync()
    {
        return await ProjectFromQueryToListAsync<TResponse>(_repository.GetAsQueryable());
    }

    public virtual async Task<List<SelectOptionResponse>> GetAllAsOptionsAsync()
    {
        return await ProjectFromQueryToListAsync<SelectOptionResponse>(_repository.GetAsQueryable());
    }


}