using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTimePeriod.query;

public class GetRoutineTimePeriodSelectOptionsEndpoint(
    AppCommandDbContext appDbContext,
    RoutineTimePeriodMapper mapper)
    : BaseGetSelectOptionsEndpoint<RoutineTimePeriod, RoutineTimePeriodMapper>(appDbContext, mapper)
{
}
