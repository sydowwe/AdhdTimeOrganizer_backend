using AdhdTimeOrganizer.Command.application.dto.request.history;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.Command.application.@interface.activityHistory;

public interface IActivityHistoryService : IEntityWithActivityService<ActivityHistory, ActivityHistoryRequest, ActivityHistoryResponse>
{
    Task<List<ActivityHistoryListGroupedByDateResponse>> FilterAsync(ActivityHistoryFilterRequest filterRequest);
}