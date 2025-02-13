using System.Linq.Expressions;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Common.domain.repositoryContract;

public interface IBaseCrudRepository<T> : IBaseReadRepository<T>
    where T : BaseEntity
{
    Task<RepositoryResult> AddAsync(T entity);
    Task<RepositoryResult> AddRangeAsync(IEnumerable<T> entity);
    Task<RepositoryResult> UpdateAsync(T entity);
    Task<RepositoryResult> DeleteAsync(long id);
    Task<RepositoryResult> DeleteAsync(T entity);
    Task<RepositoryResult> DeleteByAsync(Expression<Func<T, bool>> predicate);
    Task<RepositoryResult> BatchDeleteAsync(Expression<Func<T, bool>> predicate);
}