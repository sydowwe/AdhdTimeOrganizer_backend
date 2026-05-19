using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class MemoryAnchorFilterRequest : IFilterRequest
{
    public int? AnchorMonth { get; set; }
    public int? AnchorYear { get; set; }
    public long? ActivityId { get; set; }
}
