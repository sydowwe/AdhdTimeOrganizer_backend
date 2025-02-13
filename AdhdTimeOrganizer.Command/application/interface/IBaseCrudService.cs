using System.Linq.Expressions;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Command.application.@interface;

public interface IBaseCrudService<TEntity, in TRequest, TResponse> : IBaseReadService<TEntity, TResponse>
    where TEntity : BaseEntity
    where TRequest : IMyRequest
    where TResponse : IMyResponse
{

    Task<ServiceResult<TResponse>> InsertAsync(TEntity entity);
    Task<ServiceResult<TResponse>> InsertAsync(TRequest request);
    Task<ServiceResult<IEnumerable<TResponse>>> InsertRangeAsync(IEnumerable<TRequest> request);
    Task<ServiceResult<TResponse>> UpdateAsync(TEntity entity, TRequest request);
    Task<ServiceResult<TResponse>> UpdateAsync(long id, TRequest request);
    Task<ServiceResult> DeleteAsync(long id);
    Task<ServiceResult> DeleteByAsync(Expression<Func<TEntity, bool>> predicate);
    Task<ServiceResult> DeleteAsync(TEntity entity);
    Task<ServiceResult> BatchDeleteAsync(IEnumerable<long> idList);
}