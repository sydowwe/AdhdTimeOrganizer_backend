using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTimePeriod.command;

public class UpdateRoutineTimePeriodEndpoint(AppCommandDbContext dbContext, RoutineTimePeriodMapper mapper)
    : BaseUpdateEndpoint<RoutineTimePeriod, RoutineTimePeriodRequest, RoutineTimePeriodMapper>(dbContext, mapper);
