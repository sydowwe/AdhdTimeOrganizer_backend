using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class RoutineTodoListFilterRequest : IFilterRequest
{
    public long? ActivityId { get; set; }
    public long? TimePeriodId { get; set; }
    public bool? IsDone { get; set; }
    public long? UserId { get; set; }
}
