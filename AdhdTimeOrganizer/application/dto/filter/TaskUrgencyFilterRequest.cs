using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.filter;

public class TaskUrgencyFilterRequest : IFilterRequest
{
    public string? Text { get; set; }
    public string? Color { get; set; }
    public int? MinPriority { get; set; }
    public int? MaxPriority { get; set; }
    public long? UserId { get; set; }
}
