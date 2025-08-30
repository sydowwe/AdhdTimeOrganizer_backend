
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.filter;

public class WebExtensionDataFilterRequest : IFilterRequest
{
    public long? ActivityId { get; set; }
    public string? Domain { get; set; }
    public string? Title { get; set; }
    public int? MinDuration { get; set; }
    public int? MaxDuration { get; set; }
    public DateTime? StartTimestampAfter { get; set; }
    public DateTime? StartTimestampBefore { get; set; }
    public long? UserId { get; set; }
}
