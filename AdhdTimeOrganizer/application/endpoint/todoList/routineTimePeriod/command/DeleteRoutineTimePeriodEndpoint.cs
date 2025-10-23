using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTimePeriod.command;

public class DeleteRoutineTimePeriodEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<RoutineTimePeriod>(dbContext);
