using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.query;

public class GetSelectOptionsActivityBacklogProfileEndpoint(DbContext dbContext)
    : BaseGetSelectOptionsEndpoint<ActivityBacklogProfile>(dbContext)
{
    /// <summary>
    /// Ownership, reached through the Activity the same way the grid does it. Belt and braces on purpose:
    /// the host's <c>AppQueryFilters.ActivityProfileUserScope</c> scopes these rows too, and this slice
    /// cannot see it.
    /// </summary>
    public override IQueryable<ActivityBacklogProfile> Filter(IQueryable<ActivityBacklogProfile> query) =>
        query.Where(p => p.Activity.UserId == User.GetId());

    protected override IQueryable<ActivityBacklogProfile> Sort(IQueryable<ActivityBacklogProfile> query) =>
        query.OrderBy(p => p.Activity.Name);

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<ActivityBacklogProfile> query) =>
        query.Select(p => new SelectOptionResponse(p.Id, p.Activity.Name));
}
