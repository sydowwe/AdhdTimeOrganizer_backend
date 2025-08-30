using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTodoList.command;

public class RoutineTodoListDeleteEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<RoutineTodoList>(dbContext);
