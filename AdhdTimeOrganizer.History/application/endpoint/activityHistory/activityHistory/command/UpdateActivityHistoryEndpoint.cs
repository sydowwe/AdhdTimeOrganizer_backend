using AdhdTimeOrganizer.History.application.dto.request.history;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.command;

public class UpdateActivityHistoryEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<ActivityHistory, ActivityHistoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityHistoryValidator>();
    }
}