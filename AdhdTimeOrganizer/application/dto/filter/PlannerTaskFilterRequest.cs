using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class PlannerTaskFilterRequest : IFilterRequest
{
    public DateTime FromTimeStamp { get; set; }
    public DateTime ToTimeStamp { get; set; }


    public long? ActivityId { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
    public int? MinMinuteLength { get; set; }
    public int? MaxMinuteLength { get; set; }
    public string? Color { get; set; }
    public bool? IsDone { get; set; }
}
