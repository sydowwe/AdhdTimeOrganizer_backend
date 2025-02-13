using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.Command.domain.repositoryContract.activityHistory;

public interface IAlarmRepository: IBaseEntityWithActivityRepository<Alarm>
{
    Task<int> UpdateIsActiveByIds(IEnumerable<long> ids);
}