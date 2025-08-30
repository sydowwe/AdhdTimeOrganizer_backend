using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class RoleFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public long? UserId { get; set; }
}
