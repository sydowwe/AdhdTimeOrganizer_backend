using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

public record ActivityBacklogProfileResponse : IdResponse, IProjectionResponse<ActivityBacklogProfileResponse, ActivityBacklogProfile>
{
    public required long ActivityId { get; init; }

    /// <summary>
    /// Denormalized from the Activity so one read serves both the backlog list and the temptation-bundling
    /// pairing UI: the to-do item stores a bare activity id, and without the name here resolving it to
    /// something displayable meant a second call to /activity/all-options.
    /// </summary>
    public required string ActivityName { get; init; }

    public required long LocationTypeId { get; init; }
    public required long WeatherDependencyId { get; init; }
    public required EnergyLevel EnergyLevel { get; init; }
    public EffortType? EffortType { get; init; }
    public required int MinParticipants { get; init; }
    public int? MaxParticipants { get; init; }
    public required long ExpectedCostTierId { get; init; }
    public required int DurationMinutes { get; init; }
    public required bool IsRepeatable { get; init; }

    /// <summary>
    /// True when the user has recorded a <see cref="MemoryAnchor"/> against this profile's Activity AND the
    /// entry is one-time (<c>!IsRepeatable</c>) — anchoring marks an experience as had, which only closes a
    /// backlog entry that was meant to happen once. A repeatable entry is never "done", so this stays false
    /// for it however many anchors its activity carries. Derived on read, never stored.
    /// </summary>
    public bool IsAnchored { get; init; }

    /// <summary>
    /// The anchor behind <see cref="IsAnchored"/> — the most recent one by (year, month, id) when the
    /// activity has several. Null whenever <see cref="IsAnchored"/> is false, repeatable entries included.
    /// </summary>
    public long? MemoryAnchorId { get; init; }

    /// <summary>
    /// Leaves <see cref="IsAnchored"/>/<see cref="MemoryAnchorId"/> at their defaults: the anchors are not
    /// reachable by navigation from the profile (Activity.MemoryAnchors was deleted in the slice
    /// extraction and must stay deleted), and this signature has no DbContext to reach them with.
    /// Callers that need those two fields use <see cref="ProjectionWithAnchors"/>.
    /// </summary>
    public static IQueryable<ActivityBacklogProfileResponse> Projection(IQueryable<ActivityBacklogProfile> query)
    {
        return query.Select(e => new ActivityBacklogProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            ActivityName = e.Activity.Name,
            LocationTypeId = e.LocationTypeId,
            WeatherDependencyId = e.WeatherDependencyId,
            EnergyLevel = e.EnergyLevel,
            EffortType = e.EffortType,
            MinParticipants = e.MinParticipants,
            MaxParticipants = e.MaxParticipants,
            ExpectedCostTierId = e.ExpectedCostTierId,
            DurationMinutes = e.DurationMinutes,
            IsRepeatable = e.IsRepeatable
        });
    }

    /// <summary>
    /// <see cref="Projection"/> plus the two completion fields, computed as correlated subqueries over
    /// <paramref name="anchors"/> so both stay sortable and filterable in SQL. Pass an anchor set that is
    /// already scoped to the caller.
    /// </summary>
    public static IQueryable<ActivityBacklogProfileResponse> ProjectionWithAnchors(
        IQueryable<ActivityBacklogProfile> query,
        IQueryable<MemoryAnchor> anchors)
    {
        return query.Select(e => new ActivityBacklogProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            ActivityName = e.Activity.Name,
            LocationTypeId = e.LocationTypeId,
            WeatherDependencyId = e.WeatherDependencyId,
            EnergyLevel = e.EnergyLevel,
            EffortType = e.EffortType,
            MinParticipants = e.MinParticipants,
            MaxParticipants = e.MaxParticipants,
            ExpectedCostTierId = e.ExpectedCostTierId,
            DurationMinutes = e.DurationMinutes,
            IsRepeatable = e.IsRepeatable,
            IsAnchored = !e.IsRepeatable && anchors.Any(m => m.ActivityId == e.ActivityId),
            MemoryAnchorId = e.IsRepeatable
                ? null
                : anchors
                    .Where(m => m.ActivityId == e.ActivityId)
                    .OrderByDescending(m => m.AnchorYear)
                    .ThenByDescending(m => m.AnchorMonth)
                    .ThenByDescending(m => m.Id)
                    .Select(m => (long?)m.Id)
                    .FirstOrDefault()
        });
    }
}