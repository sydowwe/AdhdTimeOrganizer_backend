using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

/// <inheritdoc cref="ActivityBacklogProfileFilterRequest" path="/summary"/>
public record ActivityBucketListProfileFilterRequest : IFilterRequest
{
    /// <summary>Case-insensitive contains on the activity's name.</summary>
    public string? ActivityName { get; set; }

    /// <summary>Any-of over the lookup ids. Null or empty means "no opinion".</summary>
    public long[]? ExperienceTypeIds { get; set; }

    /// <summary>Inclusive lower bound on <c>ComfortZoneStep</c>.</summary>
    public int? MinComfortZoneStep { get; set; }

    /// <summary>Inclusive upper bound on <c>ComfortZoneStep</c>.</summary>
    public int? MaxComfortZoneStep { get; set; }

    public bool? RequiresTravel { get; set; }

    /// <summary>Exact match. No client sends it; the range pair above is what the chips use.</summary>
    public int? ComfortZoneStep { get; set; }

    /// <summary>
    /// Tri-state, like <see cref="RequiresTravel"/>: null does not filter, true keeps only entries whose
    /// activity has a MemoryAnchor, false keeps only the ones still to do. Also serves the "n of m
    /// experienced" readout, which asks for a page of 1 twice and reads only <c>itemsCount</c>.
    /// </summary>
    public bool? IsAnchored { get; set; }
}
