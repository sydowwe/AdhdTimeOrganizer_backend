using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class GetToDoListByIdEndpoint(
    AppCommandDbContext dbContext,
    ToDoListMapper mapper)
    : BaseGetByIdEndpoint<ToDoList, ToDoListResponse, ToDoListMapper>(dbContext, mapper)
{
    protected override IQueryable<ToDoList> WithIncludes(IQueryable<ToDoList> query)
    {
        return query
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Role)
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Category)
            .Include(tdl => tdl.TaskUrgency);
    }
}
