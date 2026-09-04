using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.query;

public class GetSelectOptionsActivityProjectProfileEndpoint(DbContext dbContext)
    : BaseGetSelectOptionsEndpoint<ActivityProjectProfile>(dbContext)
{
    /// <summary>Ownership through the Activity — see <c>GetSelectOptionsActivityBacklogProfileEndpoint</c>.</summary>
    public override IQueryable<ActivityProjectProfile> Filter(IQueryable<ActivityProjectProfile> query) =>
        query.Where(p => p.Activity.UserId == User.GetId());

    protected override IQueryable<ActivityProjectProfile> Sort(IQueryable<ActivityProjectProfile> query) =>
        query.OrderBy(p => p.Activity.Name);

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<ActivityProjectProfile> query) =>
        query.Select(p => new SelectOptionResponse(p.Id, p.Activity.Name));
}
