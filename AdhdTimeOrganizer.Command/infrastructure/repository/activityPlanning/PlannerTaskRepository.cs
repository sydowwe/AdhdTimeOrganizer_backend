using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Command.infrastructure.repository.@base;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.activityPlanning;

public class PlannerTaskRepository(AppCommandDbContext context) : BaseEntityWithIsDoneRepository<PlannerTask>(context), IPlannerTaskRepository
{
    public IQueryable<PlannerTask> GetAllByDateAndHourSpan(long userId, DateTime startDate, DateTime endDate)
    {
        return context.PlannerTasks
            .Where(task => task.UserId == userId && task.StartTimestamp >= startDate &&
                           task.StartTimestamp.AddMinutes(task.MinuteLength) <= endDate).AsQueryable();
    }
}