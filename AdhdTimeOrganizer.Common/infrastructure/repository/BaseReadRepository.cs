using System.Linq.Expressions;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.repositoryContract;
using AdhdTimeOrganizer.Common.domain.result;
using AdhdTimeOrganizer.Common.infrastructure.extension;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Common.infrastructure.repository;

public class BaseReadRepository<T, TContext>(TContext context) : BaseRepository<T, TContext>(context), IBaseReadRepository<T>
    where T : BaseEntity
    where TContext: DbContext
{
    public virtual async Task<RepositoryResult<T>> GetByIdAsync(long id, bool withoutTracking = true)
    {
        if (withoutTracking)
        {
            return await dbSet.AsNoTracking().SingleOrErrorAsync(e => e.Id == id);
        }

        var entity = await dbSet.FindAsync(id);
        return entity != null
            ? RepositoryResult<T>.Successful(entity)
            : RepositoryResult<T>.Error(RepositoryErrorType.NotFound, $"Entity with ID {id} not found");
    }
    public virtual async Task<RepositoryResult<T>> FindAsync(Expression<Func<T, bool>> predicate, bool withoutTracking = true)
    {
        return withoutTracking ? await dbSet.AsNoTracking().SingleOrErrorAsync(predicate) : await dbSet.SingleOrErrorAsync(predicate);
    }
    public virtual async Task<List<T>> GetAllAsync(bool withoutTracking = true)
    {
        return withoutTracking ? await dbSet.AsNoTracking().ToListAsync() : await dbSet.ToListAsync();
    }

    public virtual async Task<List<T>> GetAllAsync(Expression<Func<T, bool>> predicate, bool withoutTracking = true)
    {
        return withoutTracking ? await dbSet.AsNoTracking().Where(predicate).ToListAsync() : await dbSet.Where(predicate).ToListAsync();
    }

    public async Task<RepositoryResult<int>> GetCountAsync(IQueryable<BaseEntity> query)
    {
        int count;
        try
        {
            count = await query.Select(p => p.Id).CountAsync();
        }
        catch (Exception e)
        {
            return RepositoryResult<int>.Error(RepositoryErrorType.UnknownError, "Argument null exception thrown", e.Message);
        }
        return RepositoryResult<int>.Successful(count);
    }
    public virtual IQueryable<T> GetAllAsQueryable(Expression<Func<T, bool>> predicate, bool withoutTracking = true)
    {
        return withoutTracking ? dbSet.AsNoTracking().Where(predicate) : dbSet.Where(predicate);
    }
    public virtual IQueryable<T> GetAllAsQueryable(bool withoutTracking = true)
    {
        return withoutTracking ? dbSet.AsNoTracking() : dbSet.AsQueryable();
    }
}