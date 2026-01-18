using AdhdTimeOrganizer.application.endpoint.@base.command.misc;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.command;

public class ToggleIsHiddenRoutineTimePeriodEndpoint(AppCommandDbContext dbContext) : BaseToggleIsHiddenEndpoint<RoutineTimePeriod>(dbContext);