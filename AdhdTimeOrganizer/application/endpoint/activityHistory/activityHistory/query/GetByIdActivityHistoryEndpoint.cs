using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class GetByIdActivityHistoryEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<ActivityHistory, ActivityHistoryResponse>(dbContext)
{
}
