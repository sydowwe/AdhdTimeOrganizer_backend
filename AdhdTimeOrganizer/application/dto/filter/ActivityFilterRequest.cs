using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class ActivityFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public bool? IsUnavoidable { get; set; }

    public string? RoleName { get; set; }
    public string? CategoryName { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
}
