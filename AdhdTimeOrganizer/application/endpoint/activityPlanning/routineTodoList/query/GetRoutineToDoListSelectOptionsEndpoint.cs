using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTodoList.query;

public class GetRoutineTodoListSelectOptionsEndpoint(
    AppCommandDbContext appDbContext,
    RoutineTodoListMapper mapper)
    : BaseGetSelectOptionsEndpoint<RoutineTodoList, RoutineTodoListMapper>(appDbContext, mapper)
{
}
