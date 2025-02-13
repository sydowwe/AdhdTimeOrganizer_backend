using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.repositoryContract;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Common.infrastructure.repository;

public class BaseRepository<T, TContext>(TContext appDbContext) : IBaseRepository<T>
    where T : BaseEntity
    where TContext : DbContext
{
    protected readonly TContext context = appDbContext;
    protected readonly DbSet<T> dbSet = appDbContext.Set<T>();

    public virtual IQueryable<T> GetAsQueryable()
    {
        return dbSet.AsQueryable();
    }
}