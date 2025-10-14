using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.alarm.command;

public class DeleteAlarmEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<Alarm>(dbContext);
