using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command.misc;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.command;

public class ToggleIsHiddenRoutineTimePeriodEndpoint(AppDbContext dbContext) : BaseToggleIsHiddenEndpoint<RoutineTimePeriod>(dbContext);