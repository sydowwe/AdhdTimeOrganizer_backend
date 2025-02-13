using System.Linq.Expressions;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Common.domain.repositoryContract;

public interface IBaseReadRepository<T> : IBaseRepository<T>
    where T : BaseEntity
{
    Task<RepositoryResult<T>> FindAsync(Expression<Func<T, bool>> predicate, bool withoutTracking = true);
    Task<RepositoryResult<T>> GetByIdAsync(long id, bool withoutTracking = true);
    Task<List<T>> GetAllAsync(bool withoutTracking = true);
    Task<List<T>> GetAllAsync(Expression<Func<T, bool>> predicate, bool withoutTracking = true);
    Task<RepositoryResult<int>> GetCountAsync(IQueryable<BaseEntity> query);
    IQueryable<T> GetAllAsQueryable(Expression<Func<T, bool>> predicate, bool withoutTracking = true);
    IQueryable<T> GetAllAsQueryable(bool withoutTracking = true);
}