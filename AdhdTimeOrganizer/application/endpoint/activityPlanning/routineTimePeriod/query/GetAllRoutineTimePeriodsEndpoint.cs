using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class GetAllRoutineTimePeriodsEndpoint(
    AppCommandDbContext dbContext,
    RoutineTimePeriodMapper mapper)
    : BaseGetAllEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse, RoutineTimePeriodMapper>(dbContext, mapper)
{
}
