using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

public record ActivityBucketListProfileFilterRequest : IFilterRequest
{
    public bool? RequiresTravel { get; set; }
    public int? ComfortZoneStep { get; set; }
}