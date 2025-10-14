using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTodoList.command;

public class DeleteRoutineTodoListEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<RoutineTodoList>(dbContext);
