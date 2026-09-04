using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.command;

public class UpdateActivityBucketListProfileEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<ActivityBucketListProfile, ActivityBucketListProfileRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateActivityBucketListProfileValidator>();
    }

    /// <summary>Ownership through the Activity — see <c>UpdateActivityBacklogProfileEndpoint</c>.</summary>
    protected override Task<bool> AuthorizeAsync(ActivityBucketListProfile entity, CancellationToken ct = default) =>
        ownership.OwnsActivityAsync(DbContext, User.GetId(), entity.ActivityId, ct);
}
