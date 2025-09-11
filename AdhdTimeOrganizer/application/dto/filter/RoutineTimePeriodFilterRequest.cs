using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class RoutineTimePeriodFilterRequest : IFilterRequest
{
    public string? Text { get; set; }
    public string? Color { get; set; }
    public int? MinLengthInDays { get; set; }
    public int? MaxLengthInDays { get; set; }
    public bool? IsHidden { get; set; }
    public long? UserId { get; set; }
}
