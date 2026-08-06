using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.command;

public class DeleteRoutineTimePeriodEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<RoutineTimePeriod>(dbContext);