using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Command.infrastructure.repository.@base;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.activityPlanning;

public class RoutineTimePeriodRepository(AppCommandDbContext context) : BaseEntityWithUserRepository<RoutineTimePeriod>(context), IRoutineTimePeriodRepository
{
    public async Task ChangeIsHiddenInViewAsync(long id)
    {
        var timePeriod = await dbSet.FindAsync(id);
        if (timePeriod !=null)
        {
            timePeriod.IsHiddenInView = !timePeriod.IsHiddenInView;
            await context.SaveChangesAsync();
        }
    }
}