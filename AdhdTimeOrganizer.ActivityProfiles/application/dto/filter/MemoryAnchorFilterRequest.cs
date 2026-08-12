using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

public record MemoryAnchorFilterRequest : IFilterRequest
{
    public int? AnchorMonth { get; set; }
    public int? AnchorYear { get; set; }
    public long? ActivityId { get; set; }
}