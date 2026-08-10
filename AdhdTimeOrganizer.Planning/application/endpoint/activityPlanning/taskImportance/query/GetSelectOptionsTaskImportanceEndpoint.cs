using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskImportance.query;

public class GetSelectOptionsTaskImportanceEndpoint(
    DbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<TaskImportance>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TaskImportance> query)
    {
        return query.Select(t => new SelectOptionResponse(t.Id, t.Text));
    }
}