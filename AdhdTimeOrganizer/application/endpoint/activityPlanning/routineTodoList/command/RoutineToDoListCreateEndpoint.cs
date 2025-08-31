using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTodoList.command;

public class RoutineTodoListCreateEndpoint(AppCommandDbContext dbContext, RoutineTodoListMapper mapper)
    : BaseCreateEndpoint<RoutineTodoList, RoutineTodoListRequest, RoutineTodoListMapper>(dbContext, mapper);
