using AdhdTimeOrganizer.application.dto.request.history;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.command;

public class CreateActivityHistoryEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<ActivityHistory, ActivityHistoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityHistoryValidator>();
    }
}
