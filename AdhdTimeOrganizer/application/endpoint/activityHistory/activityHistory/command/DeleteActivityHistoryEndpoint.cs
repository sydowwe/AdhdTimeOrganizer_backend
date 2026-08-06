using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.command;

public class DeleteActivityHistoryEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<ActivityHistory>(dbContext);