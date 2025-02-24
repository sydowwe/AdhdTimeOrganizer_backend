using System.Linq.Expressions;
using AdhdTimeOrganizer.Command.application.@interface;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.application.service;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.repositoryContract;
using AdhdTimeOrganizer.Common.domain.result;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service;

//TODO prerobit aby vracal ServiceResult
public abstract class BaseCrudService<TEntity, TRequest, TResponse, TRepository>(
    TRepository repository,
    IMapper autoMapper
) : BaseReadService<TEntity, TResponse, TRepository>(repository,autoMapper), IBaseCrudService<TEntity, TRequest, TResponse>
    where TEntity : BaseEntity
    where TRequest : class, IMyRequest
    where TResponse : class, IMyResponse
    where TRepository : IBaseCrudRepository<TEntity>
{

    public async Task<ServiceResult<TResponse>> InsertAsync(TEntity entity)
    {
        var result = await _repository.AddAsync(entity);
        return result.Failed
            ? HandleFailedRepositoryResult<TResponse>(result)
            : ServiceResult<TResponse>.Successful(mapper.Map<TResponse>(entity));
    }
    public async Task<ServiceResult<TResponse>> InsertAsync(TRequest request)
    {
        var entity = mapper.Map<TEntity>(request);
        var result = await _repository.AddAsync(entity);
        return result.Failed
            ? HandleFailedRepositoryResult<TResponse>(result)
            : ServiceResult<TResponse>.Successful(mapper.Map<TResponse>(entity));
    }

    public async Task<ServiceResult<IEnumerable<TResponse>>> InsertRangeAsync(IEnumerable<TRequest> request)
    {
        var entities = mapper.Map<List<TEntity>>(request);
        var result = await _repository.AddRangeAsync(entities);
        return result.Failed
            ? HandleFailedRepositoryResult<IEnumerable<TResponse>>(result)
            : ServiceResult<IEnumerable<TResponse>>.Successful(mapper.Map<IEnumerable<TResponse>>(entities));
    }

    public async Task<ServiceResult<TResponse>> UpdateAsync(long id, TRequest request)
    {
        var searchResult = await _repository.GetByIdAsync(id,false);
        if (searchResult.Failed)
            return HandleFailedRepositoryResult<TResponse>(searchResult);
        var entity = searchResult.Data;

        mapper.Map(request, entity);
        var updateResult = await _repository.UpdateAsync(entity);
        return updateResult.Failed
            ? HandleFailedRepositoryResult<TResponse>(updateResult)
            : ServiceResult<TResponse>.Successful(mapper.Map<TResponse>(entity));
    }
    public async Task<ServiceResult<TResponse>> UpdateAsync(TEntity entity, TRequest request)
    {
        mapper.Map(request, entity);
        return await UpdateAsync(entity);
    }
    public async Task<ServiceResult<TResponse>> UpdateAsync(TEntity entity)
    {
        var updateResult = await _repository.UpdateAsync(entity);
        return updateResult.Failed
            ? HandleFailedRepositoryResult<TResponse>(updateResult)
            : ServiceResult<TResponse>.Successful(mapper.Map<TResponse>(entity));
    }
    public async Task<ServiceResult> DeleteAsync(long id)
    {
        var result = await _repository.DeleteAsync(id);
        return ProcessRepositoryResult(result);
    }
    public async Task<ServiceResult> DeleteByAsync(Expression<Func<TEntity, bool>> predicate)
    {
        var result = await _repository.DeleteByAsync(predicate);
        return ProcessRepositoryResult(result);
    }
    public async Task<ServiceResult> DeleteAsync(TEntity entity)
    {
        var result = await _repository.DeleteAsync(entity);
        return ProcessRepositoryResult(result);
    }

    public async Task<ServiceResult> BatchDeleteAsync(IEnumerable<long> idList)
    {
        var result = await _repository.BatchDeleteAsync(i => idList.Contains(i.Id));
        return ProcessRepositoryResult(result);
    }


}