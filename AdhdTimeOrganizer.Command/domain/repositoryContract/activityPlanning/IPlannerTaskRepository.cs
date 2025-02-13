using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;

public interface IPlannerTaskRepository : IBaseEntityWithIsDoneRepository<PlannerTask>
{
    IQueryable<PlannerTask> GetAllByDateAndHourSpan(long userId, DateTime startDate, DateTime endDate);
}