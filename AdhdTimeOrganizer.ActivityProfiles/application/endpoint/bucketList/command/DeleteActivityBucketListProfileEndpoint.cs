using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.command;

public class DeleteActivityBucketListProfileEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseDeleteEndpoint<ActivityBucketListProfile>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>Ownership through the Activity — see <c>UpdateActivityBacklogProfileEndpoint</c>.</summary>
    protected override Task<bool> AuthorizeAsync(ActivityBucketListProfile entity, CancellationToken ct = default) =>
        ownership.OwnsActivityAsync(_dbContext, User.GetId(), entity.ActivityId, ct);
}
