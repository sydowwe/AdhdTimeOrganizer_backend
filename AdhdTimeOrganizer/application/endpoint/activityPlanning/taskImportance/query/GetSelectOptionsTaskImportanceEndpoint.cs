using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.query;

public class GetSelectOptionsTaskImportanceEndpoint(
    AppDbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<TaskImportance>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TaskImportance> query)=>
        query.Select(t => new SelectOptionResponse(t.Id, t.Text));
}
