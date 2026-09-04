using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.query;

public class GetByIdActivityBacklogProfileEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseGetByIdEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>
    /// A foreign profile answers 404, not 403: a 403 would confirm the id is real, and this route is the
    /// one place a caller can probe another account's sequential profile ids one at a time.
    /// </summary>
    protected override bool NotFoundWhenUnauthorized => true;

    /// <summary>
    /// Ownership, reached through the Activity — the profile carries no <c>UserId</c> of its own. Belt and
    /// braces on purpose: the host's <c>AppQueryFilters.ActivityProfileUserScope</c> already keeps another
    /// account's row out of the projection, but this slice cannot see that filter and does not depend on it.
    /// </summary>
    protected override Task<bool> AuthorizeAsync(ActivityBacklogProfileResponse entity, CancellationToken ct) =>
        ownership.OwnsActivityAsync(_dbContext, User.GetId(), entity.ActivityId, ct);

    /// <summary>
    /// The projection has no anchors to reach (see <see cref="ActivityBacklogProfileResponse.Projection"/>),
    /// so they are overlaid from a second read rather than letting this route answer
    /// <c>isAnchored: false</c> for an entry the grid shows as done. Repeatable entries are never anchored —
    /// same rule as <c>ProjectionWithAnchors</c>.
    /// </summary>
    protected override async Task<ActivityBacklogProfileResponse> PostProcessAsync(ActivityBacklogProfileResponse entity, CancellationToken ct)
    {
        if (entity.IsRepeatable)
            return entity;

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
