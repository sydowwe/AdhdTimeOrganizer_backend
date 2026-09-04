using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.query;

public class GetSelectOptionsActivityBucketListProfileEndpoint(DbContext dbContext)
    : BaseGetSelectOptionsEndpoint<ActivityBucketListProfile>(dbContext)
{
    /// <summary>Ownership through the Activity — see <c>GetSelectOptionsActivityBacklogProfileEndpoint</c>.</summary>
    public override IQueryable<ActivityBucketListProfile> Filter(IQueryable<ActivityBucketListProfile> query) =>
        query.Where(p => p.Activity.UserId == User.GetId());

    protected override IQueryable<ActivityBucketListProfile> Sort(IQueryable<ActivityBucketListProfile> query) =>
        query.OrderBy(p => p.Activity.Name);

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<ActivityBucketListProfile> query) =>
        query.Select(p => new SelectOptionResponse(p.Id, p.Activity.Name));
}
