using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.query;

public class GetSelectOptionsTaskImportanceEndpoint(
    AppDbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<TaskImportance>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TaskImportance> query)
    {
        return query.Select(t => new SelectOptionResponse(t.Id, t.Text));
    }
}