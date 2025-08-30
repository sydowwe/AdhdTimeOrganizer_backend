using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class GetRoutineTimePeriodByIdEndpoint(
    AppCommandDbContext dbContext,
    RoutineTimePeriodMapper mapper)
    : BaseGetByIdEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse, RoutineTimePeriodMapper>(dbContext, mapper)
{
}
