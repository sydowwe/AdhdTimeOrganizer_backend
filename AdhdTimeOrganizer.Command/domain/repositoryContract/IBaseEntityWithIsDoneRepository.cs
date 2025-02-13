using AdhdTimeOrganizer.Command.domain.model.entity.@base;

namespace AdhdTimeOrganizer.Command.domain.repositoryContract;

public interface IBaseEntityWithIsDoneRepository<T> : IBaseEntityWithActivityRepository<T> where T : BaseEntityWithIsDone
{
    Task<int> UpdateIsDoneByIdsAsync(IEnumerable<long> ids);
}