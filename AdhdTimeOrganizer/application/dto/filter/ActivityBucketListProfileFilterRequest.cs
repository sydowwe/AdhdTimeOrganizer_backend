using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class ActivityBucketListProfileFilterRequest : IFilterRequest
{
    public bool? RequiresTravel { get; set; }
    public int? ComfortZoneStep { get; set; }
}
