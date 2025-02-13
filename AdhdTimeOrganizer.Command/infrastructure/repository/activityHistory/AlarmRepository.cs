using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityHistory;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Command.infrastructure.repository.@base;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.activityHistory;

public class AlarmRepository(AppCommandDbContext context) : BaseEntityWithActivityRepository<Alarm>(context), IAlarmRepository
{
    public async Task<int> UpdateIsActiveByIds(IEnumerable<long> ids)
    {
        var alarms = context.Alarms
            .Where(alarm => ids.Contains(alarm.Id))
            .ToList();
        foreach (var alarm in alarms)
        {
            alarm.IsActive = !alarm.IsActive;
        }
        return await context.SaveChangesAsync();
    }
}