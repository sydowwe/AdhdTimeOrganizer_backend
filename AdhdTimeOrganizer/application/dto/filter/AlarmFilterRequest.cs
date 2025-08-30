using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.filter;

public class AlarmFilterRequest : IFilterRequest
{
    public long? ActivityId { get; set; }
    public DateTime? StartTimestampAfter { get; set; }
    public DateTime? StartTimestampBefore { get; set; }
    public bool? IsActive { get; set; }
    public long? UserId { get; set; }
}
