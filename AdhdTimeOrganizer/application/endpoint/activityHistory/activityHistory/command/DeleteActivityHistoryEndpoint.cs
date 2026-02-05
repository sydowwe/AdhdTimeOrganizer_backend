using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.command;

public class DeleteActivityHistoryEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<ActivityHistory>(dbContext);
