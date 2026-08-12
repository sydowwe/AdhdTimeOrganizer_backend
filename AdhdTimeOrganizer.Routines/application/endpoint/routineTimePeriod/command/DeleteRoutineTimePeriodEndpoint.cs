using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.command;

public class DeleteRoutineTimePeriodEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<RoutineTimePeriod>(dbContext);