using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command.misc;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.command;

public class ToggleIsHiddenRoutineTimePeriodEndpoint(DbContext dbContext) : BaseToggleIsHiddenEndpoint<RoutineTimePeriod>(dbContext);