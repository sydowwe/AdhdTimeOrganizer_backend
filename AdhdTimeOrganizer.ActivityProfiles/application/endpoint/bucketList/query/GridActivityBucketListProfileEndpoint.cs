using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.query;

public class GridActivityBucketListProfileEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityBucketListProfile, ActivityBucketListProfileResponse, ActivityBucketListProfileFilterRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    public override string EndpointPath => "grid";

    /// <summary>
    /// The caller's anchors. Scoped by hand for the same reason the profiles are: MemoryAnchor does carry a
    /// global query filter, but this subquery decides a field the client treats as "done", and it is one
    /// line to stop depending on a filter configured in another project.
    /// </summary>
    private IQueryable<MemoryAnchor> ScopedAnchors() => _dbContext.Set<MemoryAnchor>().Where(m => m.UserId == User.GetId());

    /// <summary>
    /// Overridden so IsAnchored / MemoryAnchorId are computed in SQL rather than overlaid afterwards: the
    /// base sorts the projected queryable, so a field filled in by PostProcessItems would sort on false.
    /// </summary>
    protected override Func<IQueryable<ActivityBucketListProfile>, IQueryable<ActivityBucketListProfileResponse>> Projection =>
        query => ActivityBucketListProfileResponse.ProjectionWithAnchors(query, ScopedAnchors());

    /// <summary>
    /// Ownership belongs here, not in <see cref="ApplyCustomFiltering"/> — the base only calls that one
    /// when the request carries a filter, so a caller sending <c>useFilter: false</c> would otherwise get
    /// every user's profiles. ActivityBucketListProfile is not IEntityWithUser, so the host's
    /// AppQueryFilters.ActivityProfileUserScope — not ApplyUserQueryFilters — is what backs this up, and
    /// this slice cannot see it. The owner is reached through the Activity either way.
    /// </summary>
    protected override Task<IQueryable<ActivityBucketListProfile>> ApplyUserScoping(IQueryable<ActivityBucketListProfile> query, long userId,
        CancellationToken ct = default) =>
        Task.FromResult(query.Where(p => p.Activity.UserId == userId));

    protected override IQueryable<ActivityBucketListProfile> ApplyCustomFiltering(IQueryable<ActivityBucketListProfile> query,
        ActivityBucketListProfileFilterRequest filter)
    {
        // ILIKE over ToContainsPattern, like the activity grid: Contains is case-sensitive in Postgres and
        // ILike does not escape % / _ on its own. See FreeTextFilterExtensions.
        if (!string.IsNullOrWhiteSpace(filter.ActivityName))
        {
            var pattern = filter.ActivityName.ToContainsPattern();
            query = query.Where(p => EF.Functions.ILike(p.Activity.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        // Any-of, and an empty list is "no opinion" — the combobox sends [] when every chip is cleared.
        if (filter.ExperienceTypeIds is { Length: > 0 })
            query = query.Where(p => filter.ExperienceTypeIds.Contains(p.ExperienceTypeId));

        if (filter.MinComfortZoneStep.HasValue)
            query = query.Where(p => p.ComfortZoneStep >= filter.MinComfortZoneStep.Value);

        if (filter.MaxComfortZoneStep.HasValue)
            query = query.Where(p => p.ComfortZoneStep <= filter.MaxComfortZoneStep.Value);

        if (filter.RequiresTravel.HasValue)
            query = query.Where(p => p.RequiresTravel == filter.RequiresTravel.Value);

        if (filter.ComfortZoneStep.HasValue)
            query = query.Where(p => p.ComfortZoneStep == filter.ComfortZoneStep.Value);

        if (filter.IsAnchored.HasValue)
        {
            var anchors = ScopedAnchors();
            query = filter.IsAnchored.Value
                ? query.Where(p => anchors.Any(m => m.ActivityId == p.ActivityId))
                : query.Where(p => !anchors.Any(m => m.ActivityId == p.ActivityId));
        }

        return query;
    }
}