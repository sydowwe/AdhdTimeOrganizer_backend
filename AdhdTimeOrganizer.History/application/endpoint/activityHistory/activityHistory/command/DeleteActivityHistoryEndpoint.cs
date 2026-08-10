using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.command;

public class DeleteActivityHistoryEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<ActivityHistory>(dbContext);