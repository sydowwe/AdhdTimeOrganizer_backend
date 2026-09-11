using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;

public class ActivityBacklogProfile : BaseTableEntity, IActivityProfile
{
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;

    public long LocationTypeId { get; set; }
    public ActivityLocationType LocationType { get; set; } = null!;
    public long WeatherDependencyId { get; set; }
    public ActivityWeatherDependency WeatherDependency { get; set; } = null!;
    public EnergyLevel EnergyLevel { get; set; }
    public EffortType? EffortType { get; set; }
    public int MinParticipants { get; set; }
    public int? MaxParticipants { get; set; }
    public long ExpectedCostTierId { get; set; }
    public ActivityExpectedCostTier ExpectedCostTier { get; set; } = null!;
    public int DurationMinutes { get; set; }

    /// <summary>
    /// The activity can be done again. Stored in this direction, and exposed in it unchanged by the
    /// request, the response and the grid filter — the inverse is never offered under a second name.
    /// </summary>
    /// <remarks>
    /// "Can this be done again" is an intrinsic property of the activity; "is this one-time" is a question
    /// about the entry's lifecycle, which is what <c>IsAnchored</c> already answers. A client that wants the
    /// inverse negates it at the point of use rather than asking the API for a second spelling.
    /// </remarks>
    public bool IsRepeatable { get; set; }
}