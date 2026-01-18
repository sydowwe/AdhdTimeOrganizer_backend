using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.query;

public class GetByIdRoutineTimePeriodEndpoint(
    AppCommandDbContext dbContext,
    RoutineTimePeriodMapper mapper)
    : BaseGetByIdEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse, RoutineTimePeriodMapper>(dbContext, mapper)
{
}
