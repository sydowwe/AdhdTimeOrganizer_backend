using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.command;

public class DeleteRoutineTimePeriodEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<RoutineTimePeriod>(dbContext);
