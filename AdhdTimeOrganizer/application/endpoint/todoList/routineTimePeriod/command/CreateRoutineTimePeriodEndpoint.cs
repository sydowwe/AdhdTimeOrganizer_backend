using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.command;

public class CreateRoutineTimePeriodEndpoint(AppCommandDbContext dbContext, RoutineTimePeriodMapper mapper)
    : BaseCreateEndpoint<RoutineTimePeriod, RoutineTimePeriodRequest, RoutineTimePeriodMapper>(dbContext, mapper);
