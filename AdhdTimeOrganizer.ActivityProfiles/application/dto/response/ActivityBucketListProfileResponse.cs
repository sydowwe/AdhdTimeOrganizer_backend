using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

public record ActivityBucketListProfileResponse : IdResponse, IProjectionResponse<ActivityBucketListProfileResponse, ActivityBucketListProfile>
{
    public required long ActivityId { get; init; }
    public required long ExperienceTypeId { get; init; }
    public required int ComfortZoneStep { get; init; }
    public required bool RequiresTravel { get; init; }
    public decimal? FinancialGoal { get; init; }
    public required string InspirationSource { get; init; }

    /// <summary>
    /// True when the user has recorded a <see cref="MemoryAnchor"/> against this profile's Activity — a
    /// bucket-list entry is "done" exactly when the experience has been anchored. Derived on read, never
    /// stored: the anchor is already the record of the completion, and a second copy of that fact on the
    /// profile would be one more thing to keep in step when an anchor is created or deleted.
    /// </summary>
    public bool IsAnchored { get; init; }

    /// <summary>
    /// The anchor behind <see cref="IsAnchored"/> — the most recent one by (year, month, id) when the
    /// activity has several. Null whenever <see cref="IsAnchored"/> is false.
    /// </summary>
    public long? MemoryAnchorId { get; init; }

    /// <summary>
    /// Leaves <see cref="IsAnchored"/>/<see cref="MemoryAnchorId"/> at their defaults: the anchors are not
    /// reachable by navigation from the profile (Activity.MemoryAnchors was deleted in the slice
    /// extraction and must stay deleted), and this signature has no DbContext to reach them with.
    /// Callers that need those two fields use <see cref="ProjectionWithAnchors"/>.
    /// </summary>
    public static IQueryable<ActivityBucketListProfileResponse> Projection(IQueryable<ActivityBucketListProfile> query)
    {
        return query.Select(e => new ActivityBucketListProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            ExperienceTypeId = e.ExperienceTypeId,
            ComfortZoneStep = e.ComfortZoneStep,
            RequiresTravel = e.RequiresTravel,
            FinancialGoal = e.FinancialGoal,
            InspirationSource = e.InspirationSource
        });
    }

    /// <summary>
    /// <see cref="Projection"/> plus the two completion fields, computed as correlated subqueries over
    /// <paramref name="anchors"/> so both stay sortable and filterable in SQL. Pass an anchor set that is
    /// already scoped to the caller.
    /// </summary>
    public static IQueryable<ActivityBucketListProfileResponse> ProjectionWithAnchors(
        IQueryable<ActivityBucketListProfile> query,
        IQueryable<MemoryAnchor> anchors)
    {
        return query.Select(e => new ActivityBucketListProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            ExperienceTypeId = e.ExperienceTypeId,
            ComfortZoneStep = e.ComfortZoneStep,
            RequiresTravel = e.RequiresTravel,
            FinancialGoal = e.FinancialGoal,
            InspirationSource = e.InspirationSource,
            IsAnchored = anchors.Any(m => m.ActivityId == e.ActivityId),
            MemoryAnchorId = anchors
                .Where(m => m.ActivityId == e.ActivityId)
                .OrderByDescending(m => m.AnchorYear)
                .ThenByDescending(m => m.AnchorMonth)
                .ThenByDescending(m => m.Id)
                .Select(m => (long?)m.Id)
                .FirstOrDefault()
        });
    }
}