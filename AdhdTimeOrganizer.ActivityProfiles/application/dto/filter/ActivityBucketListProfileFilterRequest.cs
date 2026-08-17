using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

public record ActivityBucketListProfileFilterRequest : IFilterRequest
{
    public bool? RequiresTravel { get; set; }
    public int? ComfortZoneStep { get; set; }

    /// <summary>
    /// Tri-state, like <see cref="RequiresTravel"/>: null does not filter, true keeps only entries whose
    /// activity has a MemoryAnchor, false keeps only the ones still to do. Also serves the "n of m
    /// experienced" readout, which asks for a page of 1 twice and reads only <c>itemsCount</c>.
    /// </summary>
    public bool? IsAnchored { get; set; }
}