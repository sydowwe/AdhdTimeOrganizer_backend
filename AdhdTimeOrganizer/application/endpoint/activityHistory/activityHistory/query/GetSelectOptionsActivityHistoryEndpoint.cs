using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class GetSelectOptionsActivityHistoryEndpoint(
    AppCommandDbContext appDbContext,
    ActivityHistoryMapper mapper)
    : BaseGetSelectOptionsEndpoint<ActivityHistory, ActivityHistoryMapper>(appDbContext, mapper)
{
}
