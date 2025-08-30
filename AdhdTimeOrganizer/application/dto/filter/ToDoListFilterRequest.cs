using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.filter;

public class ToDoListFilterRequest : IFilterRequest
{
    public long? ActivityId { get; set; }
    public long? TaskUrgencyId { get; set; }
    public bool? IsDone { get; set; }
    public long? UserId { get; set; }
}
