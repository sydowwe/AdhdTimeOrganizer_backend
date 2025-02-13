using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Command.domain.repositoryContract;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Common.domain.result;
using AdhdTimeOrganizer.Common.infrastructure.extension;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.@base;

public class BaseEntityWithUserRepository<T>(AppCommandDbContext context) : BaseCrudRepository<T, AppCommandDbContext>(context), IBaseEntityWithUserRepository<T>
    where T : BaseEntityWithUser
{

    public async Task<RepositoryResult<T>> GetSingleByUserId(long userId)
    {
        return await dbSet.SingleOrErrorAsync(e => e.UserId == userId);
    }
    public IQueryable<T> GetAllByUserIdAsQueryable(long userId)
    {
        return dbSet.Where(u => u.UserId == userId);
    }
}