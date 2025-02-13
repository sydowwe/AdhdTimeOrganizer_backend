using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;

public interface IRoutineTimePeriodRepository : IBaseEntityWithUserRepository<RoutineTimePeriod>
{
    Task ChangeIsHiddenInViewAsync(long id);
}