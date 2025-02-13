using AdhdTimeOrganizer.Command.domain.model.entity.@base;
using AdhdTimeOrganizer.Command.domain.repositoryContract;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Common.infrastructure.persistence;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.@base;

public class BaseEntityWithIsDoneRepository<T>(AppCommandDbContext context) : BaseEntityWithActivityRepository<T>(context), IBaseEntityWithIsDoneRepository<T>
    where T : BaseEntityWithIsDone
{
    public async Task<int> UpdateIsDoneByIdsAsync(IEnumerable<long> ids)
    {
        var items = dbSet
            .Where(item => ids.Contains(item.Id))
            .ToList();
        foreach (var item in items)
        {
            item.IsDone = !item.IsDone;
        }
        return await context.SaveChangesAsync();
    }
}