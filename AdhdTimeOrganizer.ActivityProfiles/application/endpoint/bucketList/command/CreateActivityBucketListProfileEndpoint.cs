using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.command;

/// <summary>
/// No <c>BeforeMapping</c> ownership guard — <see cref="CreateActivityBucketListProfileValidator"/>
/// already establishes it. See <c>CreateActivityBacklogProfileEndpoint</c>.
/// </summary>
public class CreateActivityBucketListProfileEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<ActivityBucketListProfile, ActivityBucketListProfileRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateActivityBucketListProfileValidator>();
    }
}
