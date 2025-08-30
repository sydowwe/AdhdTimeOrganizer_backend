using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.command;

public class ActivityHistoryDeleteEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<ActivityHistory>(dbContext);
