using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Common.domain.repositoryContract;
using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Command.domain.repositoryContract;

public interface IBaseEntityWithUserRepository<T> : IBaseCrudRepository<T>
    where T : BaseEntityWithUser
{
    Task<RepositoryResult<T>> GetSingleByUserId(long userId);
    IQueryable<T> GetAllByUserIdAsQueryable(long userId);
}