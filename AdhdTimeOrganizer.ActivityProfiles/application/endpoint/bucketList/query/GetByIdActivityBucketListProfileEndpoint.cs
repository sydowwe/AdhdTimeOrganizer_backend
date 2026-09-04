using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.query;

public class GetByIdActivityBucketListProfileEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseGetByIdEndpoint<ActivityBucketListProfile, ActivityBucketListProfileResponse>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>404 rather than 403 for a foreign row — see <c>GetByIdActivityBacklogProfileEndpoint</c>.</summary>
    protected override bool NotFoundWhenUnauthorized => true;

    /// <summary>Ownership through the Activity — see <c>GetByIdActivityBacklogProfileEndpoint</c>.</summary>
    protected override Task<bool> AuthorizeAsync(ActivityBucketListProfileResponse entity, CancellationToken ct) =>
        ownership.OwnsActivityAsync(_dbContext, User.GetId(), entity.ActivityId, ct);

    /// <summary>
    /// The projection has no anchors to reach, so they are overlaid from a second read rather than letting
    /// this route answer <c>isAnchored: false</c> for an entry the grid shows as done.
    /// </summary>
    protected override async Task<ActivityBucketListProfileResponse> PostProcessAsync(ActivityBucketListProfileResponse entity, CancellationToken ct)
    {
        var anchorId = await _dbContext.Set<MemoryAnchor>()
            .Where(m => m.UserId == User.GetId() && m.ActivityId == entity.ActivityId)
            .OrderByDescending(m => m.AnchorYear)
            .ThenByDescending(m => m.AnchorMonth)
            .ThenByDescending(m => m.Id)
            .Select(m => (long?)m.Id)
            .FirstOrDefaultAsync(ct);

        return entity with { IsAnchored = anchorId is not null, MemoryAnchorId = anchorId };
    }
}
