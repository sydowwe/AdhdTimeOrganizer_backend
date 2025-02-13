using AdhdTimeOrganizer.Command.application.dto.request.history;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.Command.domain.repositoryContract.activityHistory;

public interface IActivityHistoryRepository : IBaseEntityWithActivityRepository<ActivityHistory>
{
    IQueryable<ActivityHistory> ApplyFilters(long userId, ActivityHistoryFilterRequest filter);
}