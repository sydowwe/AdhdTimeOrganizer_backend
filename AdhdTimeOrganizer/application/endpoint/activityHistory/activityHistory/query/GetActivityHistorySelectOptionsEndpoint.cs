using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory;

public class GetActivityHistorySelectOptionsEndpoint(
    AppCommandDbContext appDbContext,
    ActivityHistoryMapper mapper)
    : BaseGetSelectOptionsEndpoint<ActivityHistory, ActivityHistoryMapper>(appDbContext, mapper)
{
}
