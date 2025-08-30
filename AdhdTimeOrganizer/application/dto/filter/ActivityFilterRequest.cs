using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.filter;

public class ActivityFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public bool? IsOnToDoList { get; set; }
    public bool? IsUnavoidable { get; set; }
    public bool? IsOnRoutineToDoList { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
    public long? UserId { get; set; }
}
