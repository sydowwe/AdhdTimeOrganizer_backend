using AdhdTimeOrganizer.application.dto.request.history;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using ActivityHistoryMapper = AdhdTimeOrganizer.application.mapper.ActivityHistoryMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.command;

public class UpdateActivityHistoryEndpoint(AppDbContext dbContext, ActivityHistoryMapper mapper)
    : BaseUpdateEndpoint<ActivityHistory, ActivityHistoryRequest, ActivityHistoryMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityHistoryValidator>();
    }
}
